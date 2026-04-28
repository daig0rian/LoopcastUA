namespace LoopcastUA.Config
{
    internal class AppConfig
    {
        public SipConfig Sip { get; set; } = new SipConfig();
        public AudioConfig Audio { get; set; } = new AudioConfig();
        public SilenceDetectionConfig SilenceDetection { get; set; } = new SilenceDetectionConfig();
        public BatchConfig Batch { get; set; } = new BatchConfig();
        public ReconnectConfig Reconnect { get; set; } = new ReconnectConfig();
        public StartupConfig Startup { get; set; } = new StartupConfig();
        public LoggingConfig Logging { get; set; } = new LoggingConfig();
        public UiConfig Ui { get; set; } = new UiConfig();
    }

    internal class SipConfig
    {
        public string Server { get; set; }
        public int Port { get; set; } = 5060;
        public string Transport { get; set; } = "udp";
        public string Username { get; set; }
        public string Password { get; set; }
        public bool PasswordPlaintext { get; set; } = false;
        public string ConferenceRoom { get; set; }
        public string DisplayName { get; set; }
        public bool UseRegister { get; set; } = false;
        public int LocalRtpPort { get; set; } = 16384;
    }

    internal class AudioConfig
    {
        public string CaptureMode { get; set; } = "direct"; // "direct" | "rendered"
        public string CaptureDeviceId { get; set; } = "default";
        public int OpusBitrate { get; set; } = 48000;
        public int OpusComplexity { get; set; } = 5;
        public int FrameMs { get; set; } = 20;
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 1;
    }

    internal class SilenceDetectionConfig
    {
        public double ThresholdDbfs { get; set; } = -50.0;
        public int EnterSilenceMs { get; set; } = 1500;
        public int ExitSilenceMs { get; set; } = 300;
    }

    internal class BatchConfig
    {
        public string OnPlaybackStart { get; set; }
        public string OnPlaybackStop { get; set; }
        public int ExecutionTimeoutMs { get; set; } = 5000;
    }

    internal class ReconnectConfig
    {
        public int InitialDelayMs { get; set; } = 5000;
        public int MaxDelayMs { get; set; } = 60000;
        public double BackoffMultiplier { get; set; } = 2.0;
    }

    internal class StartupConfig
    {
        public int InitialJitterMs { get; set; } = 5000;
    }

    internal class LoggingConfig
    {
        public string Directory { get; set; } = @"%LOCALAPPDATA%\LoopcastUA\logs";
        public int MaxFileSizeMb { get; set; } = 10;
        public int MaxFiles { get; set; } = 5;
    }

    internal class UiConfig
    {
        public string Language { get; set; } = "auto"; // "auto" | "en" | "ja"
    }
}
