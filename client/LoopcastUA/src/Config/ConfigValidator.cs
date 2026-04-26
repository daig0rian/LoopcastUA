using System.Collections.Generic;
using System.IO;
using LoopcastUA.Infrastructure;

namespace LoopcastUA.Config
{
    internal class ConfigValidator
    {
        public IReadOnlyList<string> Validate(AppConfig config)
        {
            var errors = new List<string>();

            if (config?.Sip == null)
            {
                errors.Add(Strings.ValNoSip);
                return errors;
            }

            if (string.IsNullOrWhiteSpace(config.Sip.Server))
                errors.Add(Strings.ValSipServer);

            if (config.Sip.Port < 1 || config.Sip.Port > 65535)
                errors.Add(Strings.ValSipPort);

            if (string.IsNullOrWhiteSpace(config.Sip.Username))
                errors.Add(Strings.ValSipUser);

            if (string.IsNullOrWhiteSpace(config.Sip.Password))
                errors.Add(Strings.ValSipPassword);

            if (string.IsNullOrWhiteSpace(config.Sip.ConferenceRoom))
                errors.Add(Strings.ValSipRoom);

            if (config.Audio != null)
            {
                if (config.Audio.OpusBitrate < 8000 || config.Audio.OpusBitrate > 128000)
                    errors.Add(Strings.ValAudioBitrate);
            }

            if (config.SilenceDetection != null)
            {
                if (config.SilenceDetection.ThresholdDbfs < -90 || config.SilenceDetection.ThresholdDbfs > 0)
                    errors.Add(Strings.ValDetThreshold);

                if (config.SilenceDetection.EnterSilenceMs < 0)
                    errors.Add(Strings.ValDetEnter);

                if (config.SilenceDetection.ExitSilenceMs < 0)
                    errors.Add(Strings.ValDetExit);
            }

            if (config.Batch != null)
            {
                if (!string.IsNullOrEmpty(config.Batch.OnPlaybackStart) &&
                    !File.Exists(config.Batch.OnPlaybackStart))
                    errors.Add(Strings.ValBatchStart(config.Batch.OnPlaybackStart));

                if (!string.IsNullOrEmpty(config.Batch.OnPlaybackStop) &&
                    !File.Exists(config.Batch.OnPlaybackStop))
                    errors.Add(Strings.ValBatchStop(config.Batch.OnPlaybackStop));

                if (config.Batch.ExecutionTimeoutMs < 0)
                    errors.Add(Strings.ValBatchTimeout);
            }

            return errors;
        }
    }
}
