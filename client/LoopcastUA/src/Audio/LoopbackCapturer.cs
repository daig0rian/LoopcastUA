using System;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LoopcastUA.Audio
{
    internal sealed class LoopbackCapturer : IDisposable
    {
        private const int TargetSampleRate = 48000;
        private const int FrameMs = 20;
        private const int StereoFrameSamples = TargetSampleRate * FrameMs / 1000 * 2; // 1920

        private WasapiLoopbackCapture _capture;
        private BufferedWaveProvider _waveBuffer;
        private ISampleProvider _sampleSource;
        private Thread _frameThread;
        private volatile bool _running;

        public event EventHandler<float[]> StereoFrameReady;

        public void Start()
        {
            _capture = new WasapiLoopbackCapture();
            var fmt = _capture.WaveFormat;

            _waveBuffer = new BufferedWaveProvider(fmt)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
            };

            ISampleProvider samples = _waveBuffer.ToSampleProvider();

            if (fmt.Channels == 1)
                samples = new MonoToStereoSampleProvider(samples);

            if (fmt.SampleRate != TargetSampleRate)
                samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);

            _sampleSource = samples;
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();

            _running = true;
            _frameThread = new Thread(FrameLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "AudioFrameLoop",
            };
            _frameThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _frameThread?.Join(500);
            _frameThread = null;

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                try { _capture.StopRecording(); } catch { }
                _capture.Dispose();
                _capture = null;
            }

            _waveBuffer = null;
            _sampleSource = null;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            _waveBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void FrameLoop()
        {
            var sw = Stopwatch.StartNew();
            long frameCount = 0;

            while (_running)
            {
                frameCount++;
                long targetMs = frameCount * FrameMs;
                long delay = targetMs - sw.ElapsedMilliseconds;
                if (delay > 1)
                    Thread.Sleep((int)delay);

                if (!_running || _sampleSource == null) break;

                var frame = new float[StereoFrameSamples];
                _sampleSource.Read(frame, 0, StereoFrameSamples);
                StereoFrameReady?.Invoke(this, frame);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
