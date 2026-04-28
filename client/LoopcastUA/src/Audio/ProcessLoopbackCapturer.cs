using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using LoopcastUA.Infrastructure;

namespace LoopcastUA.Audio
{
    // These delegate types must be public top-level types.
    // Marshal.GetFunctionPointerForDelegate checks IsTypeVisibleFromCom; private nested types fail.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int ProcessLoopbackQI(IntPtr pThis, ref Guid riid, out IntPtr ppvObj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint ProcessLoopbackAddRef(IntPtr pThis);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint ProcessLoopbackRelease(IntPtr pThis);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int ProcessLoopbackActivateCompleted(IntPtr pThis, IntPtr pAsyncOp);
    // Used to call IActivateAudioInterfaceAsyncOperation::GetActivateResult via raw vtable,
    // bypassing the .NET RCW QI that fails for this interface type.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetActivateResultDelegate(IntPtr pThis, out int hrActivateResult, out IntPtr ppActivatedInterface);

    // Captures all process audio without being affected by the master volume.
    // Requires Windows 10 20H2 (build 19042) or later.
    internal sealed class ProcessLoopbackCapturer : ILoopbackCapturer
    {
        private const int TargetSampleRate = 48000;
        private const int FrameMs = 20;
        private const int StereoFrameSamples = TargetSampleRate * FrameMs / 1000 * 2; // 1920

        private const string VirtualLoopbackDevice = "VAD\\Process_Loopback";

        private static readonly Guid IID_IUnknown         = new Guid("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IID_CompletionHandler = new Guid("41D949AB-9862-444A-80F6-C261334DA5EB");
        private static readonly Guid IID_IAgileObject      = new Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

        private BufferedWaveProvider _waveBuffer;
        private ISampleProvider _sampleSource;
        private WaveFormat _captureFormat;
        private IAudioClient _audioClient;
        private IAudioCaptureClient _captureClient;
        private Thread _captureThread;
        private Thread _frameThread;
        private volatile bool _running;

        public event EventHandler<float[]> StereoFrameReady;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();
        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int RoInitialize(uint initType);
        [DllImport("combase.dll", PreserveSig = true)]
        private static extern void RoUninitialize();

        private const uint COINIT_MULTITHREADED = 0;
        private const uint RO_INIT_MULTITHREADED = 1;

        public void Start()
        {
            // Must be called from an MTA thread (TrayContext uses await Task.Run).
            ActivateAndInitialize();

            _running = true;

            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "ProcessLoopbackCapture",
            };
            _captureThread.Start();

            _frameThread = new Thread(FrameLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "AudioFrameLoop",
            };
            _frameThread.Start();
        }

        private void ActivateAndInitialize()
        {
            Logger.Info("[ProcessLoopback] Building activation params...");

            int hrCom = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            Logger.Info($"[ProcessLoopback] CoInitializeEx: 0x{hrCom:X8}");
            bool comInited = hrCom == 0;

            int hrRo = RoInitialize(RO_INIT_MULTITHREADED);
            Logger.Info($"[ProcessLoopback] RoInitialize: 0x{hrRo:X8}");
            bool roInited = hrRo == 0;

            // Build AUDIOCLIENT_ACTIVATION_PARAMS: 3 DWORDs = 12 bytes
            // TargetProcessId=0 is rejected by Windows (E_INVALIDARG); use our own PID with
            // EXCLUDE mode to capture all audio except this process (which renders nothing).
            int selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr paramsPtr = Marshal.AllocHGlobal(12);
            Marshal.WriteInt32(paramsPtr, 0, 1);       // ActivationType = PROCESS_LOOPBACK
            Marshal.WriteInt32(paramsPtr, 4, selfPid); // TargetProcessId = self
            Marshal.WriteInt32(paramsPtr, 8, 1);       // ProcessLoopbackMode = EXCLUDE_TARGET

            // Build PROPVARIANT for VT_BLOB
            bool is64 = IntPtr.Size == 8;
            int propVarSize = is64 ? 24 : 16;
            IntPtr propVar = Marshal.AllocHGlobal(propVarSize);
            for (int i = 0; i < propVarSize; i++) Marshal.WriteByte(propVar, i, 0);
            Marshal.WriteInt16(propVar, 0, 65);                             // vt = VT_BLOB
            Marshal.WriteInt32(propVar, 8, 12);                             // cbSize
            Marshal.WriteIntPtr(propVar, is64 ? 16 : 12, paramsPtr);       // pBlobData

            IntPtr vtable = IntPtr.Zero;
            IntPtr comObj = IntPtr.Zero;

            try
            {
                using (var doneEvent = new ManualResetEventSlim(false))
                {
                    IntPtr asyncOpPtr = IntPtr.Zero;

                    int qiCallCount = 0;
                    ProcessLoopbackQI qi = (IntPtr pThis, ref Guid riid, out IntPtr ppv) =>
                    {
                        Interlocked.Increment(ref qiCallCount);
                        if (riid == IID_IUnknown || riid == IID_CompletionHandler || riid == IID_IAgileObject)
                        { ppv = pThis; return 0; }
                        ppv = IntPtr.Zero;
                        return unchecked((int)0x80004002); // E_NOINTERFACE
                    };
                    ProcessLoopbackAddRef            addRef  = (pThis)           => 1u;
                    ProcessLoopbackRelease           release = (pThis)           => 1u;
                    ProcessLoopbackActivateCompleted done    = (pThis, pAsyncOp) =>
                    {
                        asyncOpPtr = pAsyncOp; // save for GetActivateResult
                        doneEvent.Set();
                        return 0;
                    };

                    vtable = Marshal.AllocHGlobal(4 * IntPtr.Size);
                    Marshal.WriteIntPtr(vtable, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(qi));
                    Marshal.WriteIntPtr(vtable, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(addRef));
                    Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(release));
                    Marshal.WriteIntPtr(vtable, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(done));

                    comObj = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(comObj, vtable);

                    Guid iidAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                    IntPtr asyncOpRaw; // use IntPtr to avoid .NET RCW QI on the returned interface

                    Logger.Info("[ProcessLoopback] Calling ActivateAudioInterfaceAsync...");
                    int hr = ActivateAudioInterfaceAsyncNative(
                        VirtualLoopbackDevice, ref iidAudioClient, propVar, comObj, out asyncOpRaw);
                    Logger.Info($"[ProcessLoopback] ActivateAudioInterfaceAsync HR: 0x{hr:X8}  QI calls: {qiCallCount}");
                    Marshal.ThrowExceptionForHR(hr);

                    Logger.Info("[ProcessLoopback] Waiting for ActivateCompleted callback (5 s)...");
                    if (!doneEvent.Wait(5000))
                        throw new TimeoutException("[ProcessLoopback] ActivateCompleted was not called within 5 seconds.");
                    Logger.Info("[ProcessLoopback] Activation callback received.");

                    GC.KeepAlive(qi);
                    GC.KeepAlive(addRef);
                    GC.KeepAlive(release);
                    GC.KeepAlive(done);

                    // Call GetActivateResult via raw vtable to avoid RCW QI failure.
                    // IActivateAudioInterfaceAsyncOperation vtable: [QI, AddRef, Release, GetActivateResult]
                    IntPtr vt = Marshal.ReadIntPtr(asyncOpPtr);
                    IntPtr fnPtr = Marshal.ReadIntPtr(vt, 3 * IntPtr.Size);
                    var getResult = (GetActivateResultDelegate)Marshal.GetDelegateForFunctionPointer(
                        fnPtr, typeof(GetActivateResultDelegate));

                    int hrActivate;
                    IntPtr ppActivated;
                    int hrGet = getResult(asyncOpPtr, out hrActivate, out ppActivated);
                    Logger.Info($"[ProcessLoopback] GetActivateResult: hrGet=0x{hrGet:X8} hrActivate=0x{hrActivate:X8}");
                    Marshal.ThrowExceptionForHR(hrGet);
                    Marshal.ThrowExceptionForHR(hrActivate);

                    // Wrap the returned IAudioClient* as a managed object.
                    // GetObjectForIUnknown takes ownership of the reference; do not Release separately.
                    _audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(ppActivated);
                    Logger.Info("[ProcessLoopback] IAudioClient obtained.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ProcessLoopback] Activation failed: {ex.GetType().Name}: 0x{ex.HResult:X8}: {ex.Message}");
                throw;
            }
            finally
            {
                if (comObj != IntPtr.Zero) Marshal.FreeHGlobal(comObj);
                if (vtable != IntPtr.Zero) Marshal.FreeHGlobal(vtable);
                Marshal.FreeHGlobal(propVar);
                Marshal.FreeHGlobal(paramsPtr);
                if (roInited) RoUninitialize();
                if (comInited) CoUninitialize();
            }

            try
            {
                // Process loopback virtual device may not support GetMixFormat (E_NOTIMPL).
                // Try it first; fall back to 48kHz 32-bit float stereo (the Windows mix default).
                IntPtr fmtPtr = IntPtr.Zero;
                int hrFmt = _audioClient.GetMixFormat(out fmtPtr);
                Logger.Info($"[ProcessLoopback] GetMixFormat HR: 0x{hrFmt:X8}");

                bool allocatedFmt = false;
                if (hrFmt == 0 && fmtPtr != IntPtr.Zero)
                {
                    _captureFormat = WaveFormat.MarshalFromPtr(fmtPtr);
                    Logger.Info($"[ProcessLoopback] Mix format: {_captureFormat.SampleRate} Hz, {_captureFormat.Channels} ch, {_captureFormat.BitsPerSample} bit");
                }
                else
                {
                    Logger.Info("[ProcessLoopback] GetMixFormat not available; using default 48000 Hz float32 stereo.");
                    _captureFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                    fmtPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_captureFormat));
                    Marshal.StructureToPtr(_captureFormat, fmtPtr, false);
                    allocatedFmt = true;
                }

                const int  AUDCLNT_SHAREMODE_SHARED     = 0;
                const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
                Logger.Info("[ProcessLoopback] Calling IAudioClient.Initialize...");
                int hrInit = _audioClient.Initialize(
                    AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK,
                    0L, 0L, fmtPtr, IntPtr.Zero);
                Logger.Info($"[ProcessLoopback] IAudioClient.Initialize HR: 0x{hrInit:X8}");

                if (allocatedFmt)
                    Marshal.FreeHGlobal(fmtPtr);
                else
                    Marshal.FreeCoTaskMem(fmtPtr);

                Marshal.ThrowExceptionForHR(hrInit);
                Logger.Info("[ProcessLoopback] IAudioClient.Initialize succeeded.");

                Logger.Info("[ProcessLoopback] Calling GetService (IAudioCaptureClient)...");
                Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
                object capObj;
                Marshal.ThrowExceptionForHR(_audioClient.GetService(ref iidCapture, out capObj));
                _captureClient = (IAudioCaptureClient)capObj;
                Logger.Info("[ProcessLoopback] IAudioCaptureClient obtained.");

                _waveBuffer = new BufferedWaveProvider(_captureFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(500),
                    DiscardOnBufferOverflow = true,
                };
                ISampleProvider samples = _waveBuffer.ToSampleProvider();
                if (_captureFormat.Channels == 1)
                    samples = new MonoToStereoSampleProvider(samples);
                if (_captureFormat.SampleRate != TargetSampleRate)
                    samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
                _sampleSource = samples;

                Logger.Info("[ProcessLoopback] Calling IAudioClient.Start...");
                Marshal.ThrowExceptionForHR(_audioClient.Start());
                Logger.Info("[ProcessLoopback] Capture started successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ProcessLoopback] Initialize/start failed: {ex.GetType().Name}: 0x{ex.HResult:X8}: {ex.Message}");
                throw;
            }
        }

        private void CaptureLoop()
        {
            while (_running)
            {
                uint next;
                if (_captureClient.GetNextPacketSize(out next) != 0 || next == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                IntPtr dataPtr;
                uint frames, flags;
                ulong devPos, qpcPos;
                if (_captureClient.GetBuffer(out dataPtr, out frames, out flags, out devPos, out qpcPos) != 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                try
                {
                    if (frames > 0)
                    {
                        int totalBytes = (int)(frames * (uint)_captureFormat.BlockAlign);
                        var buf = new byte[totalBytes];
                        if ((flags & 2) == 0) // AUDCLNT_BUFFERFLAGS_SILENT
                            Marshal.Copy(dataPtr, buf, 0, totalBytes);
                        _waveBuffer?.AddSamples(buf, 0, totalBytes);
                    }
                }
                finally
                {
                    _captureClient.ReleaseBuffer(frames);
                }
            }
        }

        private void FrameLoop()
        {
            var sw = Stopwatch.StartNew();
            long frameCount = 0;

            while (_running)
            {
                frameCount++;
                long delay = frameCount * FrameMs - sw.ElapsedMilliseconds;
                if (delay > 1) Thread.Sleep((int)delay);

                if (!_running || _sampleSource == null) break;

                var frame = new float[StereoFrameSamples];
                _sampleSource.Read(frame, 0, StereoFrameSamples);
                StereoFrameReady?.Invoke(this, frame);
            }
        }

        public void Stop()
        {
            _running = false;
            _frameThread?.Join(500);
            _frameThread = null;
            _captureThread?.Join(500);
            _captureThread = null;

            _waveBuffer = null;
            _sampleSource = null;

            if (_captureClient != null)
            {
                Marshal.ReleaseComObject(_captureClient);
                _captureClient = null;
            }
            if (_audioClient != null)
            {
                try { _audioClient.Stop(); } catch { }
                Marshal.ReleaseComObject(_audioClient);
                _audioClient = null;
            }
        }

        public void Dispose() => Stop();

        // ── COM interface declarations ────────────────────────────────────────

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration,
                long hnsPeriodicity, [In] IntPtr pFormat, IntPtr audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
            [PreserveSig] int GetStreamLatency(out long phnsLatency);
            [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
            [PreserveSig] int IsFormatSupported(int shareMode, [In] IntPtr pFormat, IntPtr ppClosestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
            [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead,
                out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
            [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
            [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }

        // asyncOp output uses IntPtr to prevent .NET RCW from QI-ing the returned pointer
        // (that QI fails for this interface type at runtime despite the correct GUID).
        // GetActivateResult is called via raw vtable instead (see GetActivateResultDelegate above).
        [DllImport("mmdevapi.dll", EntryPoint = "ActivateAudioInterfaceAsync")]
        private static extern int ActivateAudioInterfaceAsyncNative(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid,
            IntPtr activationParams,
            IntPtr completionHandler,
            out IntPtr activationOperation);
    }
}
