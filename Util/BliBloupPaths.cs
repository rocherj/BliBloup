using System;
using System.IO;

/// <summary>
/// Central resolution of the per-user data directory.
///
/// The BLIBLOUP_DATA_DIR environment variable overrides the default location. That keeps
/// automated tests (and portable setups) away from the real user library and settings.
///
/// On first run after the rebranding, an existing %APPDATA%\BoomBx folder is migrated once to
/// %APPDATA%\BliBloup so no library, settings, custom thumbnail or backup is lost.
/// </summary>
public static class BliBloupPaths
{
    public const string DataDirectoryVariable = "BLIBLOUP_DATA_DIR";

    private const string AppDirectoryName = "BliBloup";
    private const string LegacyDirectoryName = "BoomBx";

    private static readonly object MigrationGate = new();
    private static bool _migrationChecked;

    public static string AppDataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(roaming, AppDirectoryName);

            if (!_migrationChecked)
            {
                MigrateFromLegacy(roaming, directory);
            }

            return directory;
        }
    }

    public static string IconsDirectory => Path.Combine(AppDataDirectory, "icons");

    private static void MigrateFromLegacy(string roaming, string newDirectory)
    {
        lock (MigrationGate)
        {
            if (_migrationChecked)
            {
                return;
            }

            _migrationChecked = true;

            try
            {
                var legacyDirectory = Path.Combine(roaming, LegacyDirectoryName);

                if (!Directory.Exists(legacyDirectory))
                {
                    return;
                }

                // Never touch an existing BliBloup folder: migration only happens on first run.
                if (Directory.Exists(newDirectory) &&
                    Directory.GetFileSystemEntries(newDirectory).Length > 0)
                {
                    return;
                }

                Directory.CreateDirectory(newDirectory);

                foreach (var fileName in new[]
                         {
                             "soundboards.json",
                             "soundboards.json.bak",
                             "settings.json",
                             "settings.json.bak"
                         })
                {
                    var source = Path.Combine(legacyDirectory, fileName);
                    if (File.Exists(source))
                    {
                        File.Copy(source, Path.Combine(newDirectory, fileName));
                    }
                }

                CopyDirectory(Path.Combine(legacyDirectory, "icons"), Path.Combine(newDirectory, "icons"));
                CopyDirectory(Path.Combine(legacyDirectory, "backups"), Path.Combine(newDirectory, "backups"));

                Logger.Log("Migrated the legacy BoomBx data folder to BliBloup.");
            }
            catch (Exception ex)
            {
                // A failed migration must never block startup; defaults will be used instead.
                Logger.Log($"Legacy data migration failed: {ex}");
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));

            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
