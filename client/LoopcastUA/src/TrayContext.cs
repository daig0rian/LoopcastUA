using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoopcastUA.Audio;
using LoopcastUA.Batch;
using LoopcastUA.Config;
using LoopcastUA.Forms;
using LoopcastUA.Infrastructure;
using LoopcastUA.Sip;

namespace LoopcastUA
{
    internal sealed class TrayContext : ApplicationContext
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LoopcastUA", "config.json");

        // Hidden control for cross-thread UI marshaling (standard WinForms tray pattern)
        private readonly Control _invoker;

        private readonly ConfigStore _configStore = new ConfigStore();
        private readonly LoopbackCapturer _capturer = new LoopbackCapturer();
        private readonly SilenceDetector _silenceDetector = new SilenceDetector();
        private SipClient _sipClient = new SipClient();
        private OpusEncoder _encoder;
        private BatchRunner _batchRunner;
        private volatile bool _sipConnected;
        private TrayState _currentState = TrayState.Connecting;

        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _menuSettings;
        private ToolStripMenuItem _menuOpenLog;
        private ToolStripMenuItem _menuReloadConfig;
        private ToolStripMenuItem _menuExit;
        private readonly Icon _iconConnecting;
        private readonly Icon _iconIdle;
        private readonly Icon _iconActive;
        private readonly Icon _iconError;

        private enum TrayState { Connecting, Idle, Active, Error }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);

        public TrayContext()
        {
            _invoker = new Control();
            _invoker.CreateControl();

            _iconConnecting = CreateSpeakerIcon(Color.FromArgb(220, 180, 0), waves: false); // 黄: 接続中
            _iconIdle       = CreateSpeakerIcon(Color.FromArgb(50,  180, 50),  waves: false); // 緑: 接続済・無音
            _iconActive     = CreateSpeakerIcon(Color.FromArgb(50,  210, 50),  waves: true);  // 緑+波: 再生中
            _iconError      = CreateSpeakerIcon(Color.FromArgb(210, 50,  50),  waves: false); // 赤: エラー

            _configStore.Load(ConfigPath);
            var config = _configStore.Current;

            Strings.SetLang(Strings.ParseCode(config.Ui?.Language));

            Logger.Initialize(
                Environment.ExpandEnvironmentVariables(config.Logging.Directory),
                config.Logging.MaxFileSizeMb,
                config.Logging.MaxFiles);

            _encoder = new OpusEncoder(config.Audio.OpusBitrate, config.Audio.OpusComplexity);
            _batchRunner = new BatchRunner(config);

            _silenceDetector.UpdateConfig(
                config.SilenceDetection.ThresholdDbfs,
                config.SilenceDetection.EnterSilenceMs,
                config.SilenceDetection.ExitSilenceMs);

            _statusItem = new ToolStripMenuItem(Strings.StatusConnecting) { Enabled = false };

            _menuSettings    = new ToolStripMenuItem(Strings.MenuSettings,     null, OnSettings);
            _menuOpenLog     = new ToolStripMenuItem(Strings.MenuOpenLog,      null, OnOpenLogFolder);
            _menuReloadConfig = new ToolStripMenuItem(Strings.MenuReloadConfig, null, OnReloadConfig);
            _menuExit        = new ToolStripMenuItem(Strings.MenuExit,         null, OnExit);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_menuSettings);
            menu.Items.Add(_menuOpenLog);
            menu.Items.Add(_menuReloadConfig);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_menuExit);

            _trayIcon = new NotifyIcon
            {
                Icon = _iconConnecting,
                Text = Strings.TipConnecting,
                ContextMenuStrip = menu,
                Visible = true,
            };

            WireAudioEvents();
            WireSipEvents(_sipClient);
            _configStore.ConfigChanged += OnConfigChanged;

            _ = StartPipelineAsync(config);
        }

        private static Icon CreateSpeakerIcon(Color color, bool waves)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (var brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, 1, 6, 3, 4);
                    g.FillPolygon(brush, new PointF[]
                    {
                        new PointF(4, 6), new PointF(9, 3),
                        new PointF(9, 12), new PointF(4, 10),
                    });
                }

                if (waves)
                {
                    using (var pen = new Pen(color, 1.5f)
                    {
                        StartCap = System.Drawing.Drawing2D.LineCap.Round,
                        EndCap   = System.Drawing.Drawing2D.LineCap.Round,
                    })
                    {
                        g.DrawArc(pen, 10, 5, 3, 5, -50, 100);
                        g.DrawArc(pen, 12, 3, 4, 9, -50, 100);
                    }
                }

                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void WireAudioEvents()
        {
            _capturer.StereoFrameReady += OnStereoFrame;
            _silenceDetector.PlaybackStarted += (_, __) =>
            {
                _batchRunner.RunOnPlaybackStart();
                if (_sipConnected) SetState(TrayState.Active);
            };
            _silenceDetector.PlaybackStopped += (_, __) =>
            {
                _batchRunner.RunOnPlaybackStop();
                if (_sipConnected) SetState(TrayState.Idle);
            };
        }

        private void WireSipEvents(SipClient sip)
        {
            sip.CallConnected += (_, __) =>
            {
                _sipConnected = true;
                SetState(_silenceDetector.IsPlaying ? TrayState.Active : TrayState.Idle);
            };
            sip.CallDisconnected += (_, __) =>
            {
                _sipConnected = false;
                SetState(TrayState.Error);
            };
        }

        private void OnStereoFrame(object sender, float[] stereo)
        {
            var mono = AudioMixer.MixStereoToMono(stereo);
            _silenceDetector.Feed(mono);
            var encoded = _encoder.Encode(mono);
            _sipClient.RtpSender.Send(encoded);
        }

        private async Task StartPipelineAsync(AppConfig config)
        {
            if (string.IsNullOrEmpty(config.Sip?.Server))
            {
                _invoker.BeginInvoke(new Action(PromptFirstRun));
                return;
            }

            int jitter = config.Startup?.InitialJitterMs ?? 0;
            if (jitter > 0)
                await Task.Delay(new Random().Next(0, jitter));

            _capturer.Start();
            _sipClient.Start(config);

            // Trim working set after startup completes
            await Task.Delay(5000);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            try
            {
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        private void PromptFirstRun()
        {
            MessageBox.Show(Strings.FirstRunBody, Strings.FirstRunTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnSettings(null, EventArgs.Empty);
        }

        private void OnConfigChanged(object sender, AppConfig config)
        {
            _batchRunner.UpdateConfig(config);
            _silenceDetector.UpdateConfig(
                config.SilenceDetection.ThresholdDbfs,
                config.SilenceDetection.EnterSilenceMs,
                config.SilenceDetection.ExitSilenceMs);

            Strings.SetLang(Strings.ParseCode(config.Ui?.Language));
            RefreshMenuStrings();
        }

        private void RefreshMenuStrings()
        {
            void Apply()
            {
                _menuSettings.Text     = Strings.MenuSettings;
                _menuOpenLog.Text      = Strings.MenuOpenLog;
                _menuReloadConfig.Text = Strings.MenuReloadConfig;
                _menuExit.Text         = Strings.MenuExit;
                SetState(_currentState);
            }
            if (_invoker.InvokeRequired)
                _invoker.BeginInvoke(new Action(Apply));
            else
                Apply();
        }

        private void SetState(TrayState state)
        {
            _currentState = state;

            void Apply()
            {
                switch (state)
                {
                    case TrayState.Connecting:
                        _trayIcon.Icon   = _iconConnecting;
                        _trayIcon.Text   = Strings.TipConnecting;
                        _statusItem.Text = Strings.StatusConnecting;
                        break;
                    case TrayState.Idle:
                        _trayIcon.Icon   = _iconIdle;
                        _trayIcon.Text   = Strings.TipIdle;
                        _statusItem.Text = Strings.StatusIdle;
                        break;
                    case TrayState.Active:
                        _trayIcon.Icon   = _iconActive;
                        _trayIcon.Text   = Strings.TipPlaying;
                        _statusItem.Text = Strings.StatusPlaying;
                        break;
                    case TrayState.Error:
                        _trayIcon.Icon   = _iconConnecting;
                        _trayIcon.Text   = Strings.TipReconnecting;
                        _statusItem.Text = Strings.StatusReconnecting;
                        break;
                }
            }

            if (_invoker.InvokeRequired)
                _invoker.BeginInvoke(new Action(Apply));
            else
                Apply();
        }

        private void OnSettings(object sender, EventArgs e)
        {
            var prevConfig = _configStore.Current;
            using (var form = new SettingsForm(_configStore, ConfigPath))
                form.ShowDialog();

            var newConfig = _configStore.Current;
            if (SipOrAudioChanged(prevConfig, newConfig))
                RestartPipeline(newConfig);
        }

        private static bool SipOrAudioChanged(AppConfig a, AppConfig b)
        {
            return a.Sip.Server != b.Sip.Server
                || a.Sip.Port != b.Sip.Port
                || a.Sip.Username != b.Sip.Username
                || a.Sip.Password != b.Sip.Password
                || a.Sip.ConferenceRoom != b.Sip.ConferenceRoom
                || a.Audio.OpusBitrate != b.Audio.OpusBitrate
                || a.Audio.OpusComplexity != b.Audio.OpusComplexity;
        }

        private void RestartPipeline(AppConfig config)
        {
            _sipConnected = false;
            _capturer.Stop();
            _sipClient.Dispose();
            SetState(TrayState.Connecting);

            _encoder.Dispose();
            _encoder = new OpusEncoder(config.Audio.OpusBitrate, config.Audio.OpusComplexity);

            _sipClient = new SipClient();
            WireSipEvents(_sipClient);

            _ = StartPipelineAsync(config);
        }

        private void OnOpenLogFolder(object sender, EventArgs e)
        {
            var dir = Environment.ExpandEnvironmentVariables(
                _configStore.Current?.Logging?.Directory ?? "");
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start(dir);
            else
                MessageBox.Show(Strings.LogFolderNotFound + dir, Strings.AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnReloadConfig(object sender, EventArgs e)
        {
            _configStore.Load(ConfigPath);
            var config = _configStore.Current;
            _batchRunner.UpdateConfig(config);
            _silenceDetector.UpdateConfig(
                config.SilenceDetection.ThresholdDbfs,
                config.SilenceDetection.EnterSilenceMs,
                config.SilenceDetection.ExitSilenceMs);
            Strings.SetLang(Strings.ParseCode(config.Ui?.Language));
            RefreshMenuStrings();
            MessageBox.Show(Strings.ConfigReloaded, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object sender, EventArgs e)
        {
            _sipClient.Stop();
            _capturer.Stop();
            _trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sipClient?.Dispose();
                _capturer?.Dispose();
                _encoder?.Dispose();
                _trayIcon?.Dispose();
                _invoker?.Dispose();
                _iconConnecting?.Dispose();
                _iconIdle?.Dispose();
                _iconActive?.Dispose();
                _iconError?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
