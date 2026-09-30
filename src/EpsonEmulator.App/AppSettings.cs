using System;
using System.IO;
using System.Text.Json;

namespace EpsonEmulator.App
{
    internal sealed class AppSettings
    {
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 9100;
        public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EscPosJobs");

        public static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EpsonEmulator",
                    "settings.json");
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
