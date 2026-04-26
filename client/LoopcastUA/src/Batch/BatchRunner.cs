using System;
using System.Diagnostics;
using System.Threading;
using LoopcastUA.Config;
using LoopcastUA.Infrastructure;

namespace LoopcastUA.Batch
{
    internal sealed class BatchRunner
    {
        private volatile string _onStart;
        private volatile string _onStop;
        private volatile int _timeoutMs;

        public BatchRunner(AppConfig config) => UpdateConfig(config);

        public void UpdateConfig(AppConfig config)
        {
            _onStart = config?.Batch?.OnPlaybackStart;
            _onStop = config?.Batch?.OnPlaybackStop;
            _timeoutMs = config?.Batch?.ExecutionTimeoutMs ?? 5000;
        }

        public void RunOnPlaybackStart() => Run(_onStart);
        public void RunOnPlaybackStop() => Run(_onStop);

        private void Run(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath)) return;
            int timeout = _timeoutMs;
            ThreadPool.QueueUserWorkItem(__ =>
            {
                try
                {
                    var psi = new ProcessStartInfo(scriptPath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null && !proc.WaitForExit(timeout))
                        {
                            proc.Kill();
                            Logger.Warn($"Batch timeout: {scriptPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Batch error '{scriptPath}': {ex.Message}");
                }
            });
        }
    }
}
