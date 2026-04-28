using Microsoft.Win32;
using LoopcastUA.Config;

namespace LoopcastUA.Audio
{
    internal static class LoopbackCapturerFactory
    {
        // Windows 10 20H2 = build 19042 (first build with AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK)
        private const int MinBuild = 19042;

        public static int GetWindowsBuild()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key?.GetValue("CurrentBuildNumber") is string s && int.TryParse(s, out int b))
                        return b;
                }
            }
            catch { }
            return 0;
        }

        public static bool IsDirectCaptureSupported() => GetWindowsBuild() >= MinBuild;

        public static ILoopbackCapturer Create(AppConfig config)
        {
            string mode = config.Audio?.CaptureMode ?? "direct";
            if (mode == "direct" && IsDirectCaptureSupported())
                return new ProcessLoopbackCapturer();

            string deviceId = config.Audio?.CaptureDeviceId ?? "default";
            return new LoopbackCapturer(deviceId);
        }
    }
}
