using System;

namespace LoopcastUA.Audio
{
    internal sealed class SilenceDetector
    {
        private const int FrameMs = 20;

        private volatile SilenceConfig _config;

        private class SilenceConfig
        {
            public double ThresholdDbfs;
            public int EnterSilenceMs;
            public int ExitSilenceMs;
        }

        private bool _isPlaying;
        private int _silenceFrames;
        private int _playingFrames;

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackStopped;

        public bool IsPlaying => _isPlaying;

        public SilenceDetector(double thresholdDbfs = -50.0, int enterSilenceMs = 1500, int exitSilenceMs = 300)
        {
            _config = new SilenceConfig { ThresholdDbfs = thresholdDbfs, EnterSilenceMs = enterSilenceMs, ExitSilenceMs = exitSilenceMs };
        }

        public void UpdateConfig(double thresholdDbfs, int enterSilenceMs, int exitSilenceMs)
        {
            _config = new SilenceConfig { ThresholdDbfs = thresholdDbfs, EnterSilenceMs = enterSilenceMs, ExitSilenceMs = exitSilenceMs };
        }

        public void Feed(float[] monoSamples)
        {
            var cfg = _config;
            double threshold = cfg.ThresholdDbfs;
            int enterMs = cfg.EnterSilenceMs;
            int exitMs = cfg.ExitSilenceMs;

            double rms = ComputeRms(monoSamples);
            double dbfs = rms > 1e-9 ? 20.0 * Math.Log10(rms) : -200.0;
            bool loud = dbfs >= threshold;

            if (_isPlaying)
            {
                if (loud)
                {
                    _silenceFrames = 0;
                }
                else
                {
                    _silenceFrames++;
                    if (_silenceFrames * FrameMs >= enterMs)
                    {
                        _isPlaying = false;
                        _silenceFrames = 0;
                        _playingFrames = 0;
                        PlaybackStopped?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            else
            {
                if (!loud)
                {
                    _playingFrames = 0;
                }
                else
                {
                    _playingFrames++;
                    if (_playingFrames * FrameMs >= exitMs)
                    {
                        _isPlaying = true;
                        _playingFrames = 0;
                        _silenceFrames = 0;
                        PlaybackStarted?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        private static double ComputeRms(float[] samples)
        {
            if (samples.Length == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * (double)samples[i];
            return Math.Sqrt(sum / samples.Length);
        }
    }
}
