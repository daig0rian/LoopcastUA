using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using LoopcastUA.Config;
using LoopcastUA.Infrastructure;

namespace LoopcastUA.Forms
{
    internal sealed class SettingsForm : Form
    {
        private readonly ConfigStore _configStore;
        private readonly string _configPath;

        private TextBox _sipServer, _sipUsername, _sipPassword, _sipRoom, _sipDisplayName;
        private NumericUpDown _sipPort;

        private ComboBox _audioDevice;
        private NumericUpDown _opusBitrate;

        private NumericUpDown _thresholdDbfs, _enterSilenceMs, _exitSilenceMs;

        private TextBox _batchOnStart, _batchOnStop;
        private NumericUpDown _batchTimeout;

        private ComboBox _uiLanguage;

        public SettingsForm(ConfigStore configStore, string configPath)
        {
            _configStore = configStore;
            _configPath = configPath;
            BuildUI();
            LoadFromConfig(_configStore.Current);
        }

        private void BuildUI()
        {
            Text = Strings.SettingsTitle;
            Size = new Size(520, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildSipTab());
            tabs.TabPages.Add(BuildAudioTab());
            tabs.TabPages.Add(BuildDetectionTab());
            tabs.TabPages.Add(BuildBatchTab());
            tabs.TabPages.Add(BuildGeneralTab());

            var btnOk     = new Button { Text = Strings.BtnOk,     Width = 80 };
            var btnApply  = new Button { Text = Strings.BtnApply,  Width = 80 };
            var btnCancel = new Button { Text = Strings.BtnCancel, Width = 90, DialogResult = DialogResult.Cancel };

            btnOk.Click    += (_, __) => { if (SaveConfig()) DialogResult = DialogResult.OK; };
            btnApply.Click += (_, __) => SaveConfig();

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(4),
            };
            btnPanel.Controls.AddRange(new Control[] { btnCancel, btnApply, btnOk });

            Controls.Add(tabs);
            Controls.Add(btnPanel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        // ---- Tab builders ----

        private TabPage BuildSipTab()
        {
            var tab = new TabPage(Strings.TabSip);
            var t = MakeTable(6);
            int r = 0;
            _sipServer      = AddText(t, r++, Strings.LabelServer);
            _sipPort        = AddNumeric(t, r++, Strings.LabelPort, 1, 65535, 0, 1);
            _sipUsername    = AddText(t, r++, Strings.LabelExtension);
            _sipPassword    = AddPassword(t, r++, Strings.LabelPassword);
            _sipRoom        = AddText(t, r++, Strings.LabelRoom);
            _sipDisplayName = AddText(t, r++, Strings.LabelDisplayName);
            tab.Controls.Add(t);
            return tab;
        }

        private TabPage BuildAudioTab()
        {
            var tab = new TabPage(Strings.TabAudio);
            var t = MakeTable(2);
            int r = 0;
            _audioDevice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            PopulateAudioDevices();
            AddRow(t, r++, Strings.LabelCaptureDevice, _audioDevice);
            _opusBitrate = AddNumeric(t, r++, Strings.LabelOpusBitrate, 8000, 128000, 0, 1000);
            tab.Controls.Add(t);
            return tab;
        }

        private TabPage BuildDetectionTab()
        {
            var tab = new TabPage(Strings.TabDetection);
            var t = MakeTable(3);
            int r = 0;
            _thresholdDbfs  = AddNumeric(t, r++, Strings.LabelThreshold,    -90, 0,     1, 0.5m);
            _enterSilenceMs = AddNumeric(t, r++, Strings.LabelEnterSilence,   0, 30000, 0, 100);
            _exitSilenceMs  = AddNumeric(t, r++, Strings.LabelExitSilence,    0, 30000, 0, 100);
            tab.Controls.Add(t);
            return tab;
        }

        private TabPage BuildBatchTab()
        {
            var tab = new TabPage(Strings.TabBatch);
            var t = MakeTable(3);
            int r = 0;
            _batchOnStart = AddBrowse(t, r++, Strings.LabelOnStart);
            _batchOnStop  = AddBrowse(t, r++, Strings.LabelOnStop);
            _batchTimeout = AddNumeric(t, r++, Strings.LabelTimeout, 0, 60000, 0, 500);
            tab.Controls.Add(t);
            return tab;
        }

        private TabPage BuildGeneralTab()
        {
            var tab = new TabPage(Strings.TabGeneral);
            var t = MakeTable(1);

            _uiLanguage = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _uiLanguage.Items.Add(new LangItem("auto", Strings.LangAuto));
            _uiLanguage.Items.Add(new LangItem("en",   Strings.LangEn));
            _uiLanguage.Items.Add(new LangItem("ja",   Strings.LangJa));
            AddRow(t, 0, Strings.LabelLanguage, _uiLanguage);

            tab.Controls.Add(t);
            return tab;
        }

        // ---- Load / Save ----

        private void LoadFromConfig(AppConfig cfg)
        {
            _sipServer.Text      = cfg.Sip?.Server ?? "";
            _sipPort.Value       = Clamp(cfg.Sip?.Port ?? 5060, 1, 65535);
            _sipUsername.Text    = cfg.Sip?.Username ?? "";
            _sipPassword.Text    = cfg.Sip?.Password ?? "";
            _sipRoom.Text        = cfg.Sip?.ConferenceRoom ?? "";
            _sipDisplayName.Text = cfg.Sip?.DisplayName ?? "";

            SelectAudioDevice(cfg.Audio?.CaptureDeviceId ?? "default");
            _opusBitrate.Value = Clamp(cfg.Audio?.OpusBitrate ?? 48000, 8000, 128000);

            _thresholdDbfs.Value  = (decimal)Math.Min(0, Math.Max(-90, cfg.SilenceDetection?.ThresholdDbfs ?? -50.0));
            _enterSilenceMs.Value = Clamp(cfg.SilenceDetection?.EnterSilenceMs ?? 1500, 0, 30000);
            _exitSilenceMs.Value  = Clamp(cfg.SilenceDetection?.ExitSilenceMs ?? 300,   0, 30000);

            _batchOnStart.Text  = cfg.Batch?.OnPlaybackStart ?? "";
            _batchOnStop.Text   = cfg.Batch?.OnPlaybackStop  ?? "";
            _batchTimeout.Value = Clamp(cfg.Batch?.ExecutionTimeoutMs ?? 5000, 0, 60000);

            SelectLanguage(cfg.Ui?.Language ?? "auto");
        }

        private bool SaveConfig()
        {
            var config = BuildConfig();
            var errors = new ConfigValidator().Validate(config);
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    Strings.ValidationBody + "\n\n• " + string.Join("\n• ", errors),
                    Strings.ValidationTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            _configStore.Save(_configPath, config);
            return true;
        }

        private AppConfig BuildConfig()
        {
            var prev = _configStore.Current;
            string deviceId = _audioDevice.SelectedItem is AudioDeviceItem d ? d.Id : "default";
            string langCode = _uiLanguage.SelectedItem is LangItem l ? l.Code : "auto";

            return new AppConfig
            {
                Sip = new SipConfig
                {
                    Server         = _sipServer.Text.Trim(),
                    Port           = (int)_sipPort.Value,
                    Username       = _sipUsername.Text.Trim(),
                    Password       = _sipPassword.Text,
                    ConferenceRoom = _sipRoom.Text.Trim(),
                    DisplayName    = _sipDisplayName.Text.Trim(),
                    Transport       = prev.Sip?.Transport ?? "udp",
                    UseRegister     = prev.Sip?.UseRegister ?? false,
                    LocalRtpPort    = prev.Sip?.LocalRtpPort ?? 16384,
                    PasswordPlaintext = prev.Sip?.PasswordPlaintext ?? false,
                },
                Audio = new AudioConfig
                {
                    CaptureDeviceId = deviceId,
                    OpusBitrate     = (int)_opusBitrate.Value,
                    OpusComplexity  = prev.Audio?.OpusComplexity ?? 5,
                    FrameMs         = 20,
                    SampleRate      = 48000,
                    Channels        = 1,
                },
                SilenceDetection = new SilenceDetectionConfig
                {
                    ThresholdDbfs  = (double)_thresholdDbfs.Value,
                    EnterSilenceMs = (int)_enterSilenceMs.Value,
                    ExitSilenceMs  = (int)_exitSilenceMs.Value,
                },
                Batch = new BatchConfig
                {
                    OnPlaybackStart    = NullIfEmpty(_batchOnStart.Text),
                    OnPlaybackStop     = NullIfEmpty(_batchOnStop.Text),
                    ExecutionTimeoutMs = (int)_batchTimeout.Value,
                },
                Reconnect = prev.Reconnect ?? new ReconnectConfig(),
                Startup   = prev.Startup   ?? new StartupConfig(),
                Logging   = prev.Logging   ?? new LoggingConfig(),
                Ui        = new UiConfig { Language = langCode },
            };
        }

        // ---- Audio device helpers ----

        private struct AudioDeviceItem
        {
            public readonly string Id;
            public readonly string Name;
            public AudioDeviceItem(string id, string name) { Id = id; Name = name; }
            public override string ToString() => Name;
        }

        private void PopulateAudioDevices()
        {
            _audioDevice.Items.Add(new AudioDeviceItem("default", Strings.DefaultDevice));
            try
            {
                using (var en = new MMDeviceEnumerator())
                {
                    foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                        _audioDevice.Items.Add(new AudioDeviceItem(dev.ID, dev.FriendlyName));
                }
            }
            catch { /* WASAPI enumeration failed, "Default" only */ }

            if (_audioDevice.Items.Count > 0)
                _audioDevice.SelectedIndex = 0;
        }

        private void SelectAudioDevice(string id)
        {
            for (int i = 0; i < _audioDevice.Items.Count; i++)
            {
                if (_audioDevice.Items[i] is AudioDeviceItem item && item.Id == id)
                {
                    _audioDevice.SelectedIndex = i;
                    return;
                }
            }
            _audioDevice.SelectedIndex = 0;
        }

        // ---- Language helpers ----

        private struct LangItem
        {
            public readonly string Code;
            public readonly string DisplayName;
            public LangItem(string code, string name) { Code = code; DisplayName = name; }
            public override string ToString() => DisplayName;
        }

        private void SelectLanguage(string code)
        {
            for (int i = 0; i < _uiLanguage.Items.Count; i++)
            {
                if (_uiLanguage.Items[i] is LangItem item && item.Code == code)
                {
                    _uiLanguage.SelectedIndex = i;
                    return;
                }
            }
            _uiLanguage.SelectedIndex = 0;
        }

        // ---- Layout helpers ----

        private static TableLayoutPanel MakeTable(int rows)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = rows + 1,  // +1 filler row absorbs extra TabPage height
                Padding = new Padding(8),
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rows; i++)
                t.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // filler
            return t;
        }

        private static void AddRow(TableLayoutPanel t, int row, string label, Control ctrl)
        {
            t.Controls.Add(new Label
            {
                Text = label + ":",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
            }, 0, row);
            t.Controls.Add(ctrl, 1, row);
        }

        private static TextBox AddText(TableLayoutPanel t, int row, string label)
        {
            var tb = new TextBox { Dock = DockStyle.Fill };
            AddRow(t, row, label, tb);
            return tb;
        }

        private static TextBox AddPassword(TableLayoutPanel t, int row, string label)
        {
            var tb = new TextBox { Dock = DockStyle.Fill, PasswordChar = '*' };
            AddRow(t, row, label, tb);
            return tb;
        }

        private static NumericUpDown AddNumeric(TableLayoutPanel t, int row, string label,
            decimal min, decimal max, int decimalPlaces, decimal increment)
        {
            var nud = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimalPlaces,
                Increment = increment,
                Value = min,
            };
            AddRow(t, row, label, nud);
            return nud;
        }

        private TextBox AddBrowse(TableLayoutPanel t, int row, string label)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var tb  = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Top = 5 };
            var btn = new Button  { Text = "...", Width = 30, Anchor = AnchorStyles.Right | AnchorStyles.Top, Top = 4 };

            panel.Controls.Add(tb);
            panel.Controls.Add(btn);
            panel.Layout += (_, __) =>
            {
                btn.Left = panel.Width - btn.Width - 2;
                tb.Left  = 0;
                tb.Width = panel.Width - btn.Width - 6;
            };
            btn.Click += (_, __) =>
            {
                using (var dlg = new OpenFileDialog
                {
                    Filter   = "Scripts (*.bat;*.cmd;*.exe;*.ps1)|*.bat;*.cmd;*.exe;*.ps1|All files (*.*)|*.*",
                    FileName = tb.Text,
                })
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                        tb.Text = dlg.FileName;
                }
            };

            AddRow(t, row, label, panel);
            return tb;
        }

        // ---- Utilities ----

        private static decimal Clamp(decimal value, decimal min, decimal max) =>
            value < min ? min : value > max ? max : value;

        private static decimal Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;

        private static string NullIfEmpty(string s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
