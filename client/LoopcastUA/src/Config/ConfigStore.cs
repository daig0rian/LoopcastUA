using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LoopcastUA.Config
{
    internal class ConfigStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public event EventHandler<AppConfig> ConfigChanged;

        public AppConfig Current { get; private set; } = new AppConfig();

        public void Load(string path)
        {
            if (!File.Exists(path))
                return;
            var json = File.ReadAllText(path);
            var config = JsonConvert.DeserializeObject<AppConfig>(json, JsonSettings) ?? new AppConfig();
            DecryptPassword(config);
            Current = config;
        }

        public void Save(string path, AppConfig config)
        {
            var toSave = RoundTripClone(config);
            EncryptPassword(toSave);
            var json = JsonConvert.SerializeObject(toSave, JsonSettings);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, json);
            Current = config;
            ConfigChanged?.Invoke(this, config);
        }

        private static void DecryptPassword(AppConfig config)
        {
            if (config.Sip == null || config.Sip.PasswordPlaintext) return;
            if (DpapiProtector.IsProtected(config.Sip.Password))
                config.Sip.Password = DpapiProtector.Unprotect(config.Sip.Password);
        }

        private static void EncryptPassword(AppConfig config)
        {
            if (config.Sip == null || config.Sip.PasswordPlaintext) return;
            if (!DpapiProtector.IsProtected(config.Sip.Password))
                config.Sip.Password = DpapiProtector.Protect(config.Sip.Password);
        }

        private static AppConfig RoundTripClone(AppConfig config)
        {
            var json = JsonConvert.SerializeObject(config, JsonSettings);
            return JsonConvert.DeserializeObject<AppConfig>(json, JsonSettings) ?? new AppConfig();
        }
    }
}
