using System;
using System.IO;
using System.Text.Json;
using BliBloup.Models;

namespace BliBloup.Services
{
    public static class SettingsManager
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string AppDataDir => BliBloupPaths.AppDataDirectory;

        public static AppSettings LoadSettings()
        {
            Directory.CreateDirectory(AppDataDir);
            var path = Path.Combine(AppDataDir, "settings.json");

            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                // A corrupt settings file must not block startup: fall back to defaults.
                Logger.Log($"Settings load failed, using defaults: {ex.Message}");
                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            Directory.CreateDirectory(AppDataDir);
            var path = Path.Combine(AppDataDir, "settings.json");
            var tempPath = path + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, WriteOptions);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, path + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                Logger.Log($"Atomic settings replace unavailable, using copy fallback: {ex.Message}");
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }
            }
        }
    }
}
