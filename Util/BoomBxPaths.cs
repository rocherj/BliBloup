using System;
using System.IO;

/// <summary>
/// Central resolution of the per-user data directory.
///
/// The BOOMBX_DATA_DIR environment variable overrides the default location. That keeps
/// automated tests (and portable setups) away from the real user library and settings.
/// </summary>
public static class BoomBxPaths
{
    public const string DataDirectoryVariable = "BOOMBX_DATA_DIR";

    public static string AppDataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoomBx");
        }
    }

    public static string IconsDirectory => Path.Combine(AppDataDirectory, "icons");
}
