namespace KassenSync.Core.Services;

public static class AppPaths
{
    public const string ProductName = "OrdnerSync";
    public const string LegacyProductName = "KassenSync";

    private static string CommonData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static string DataDirectory => Path.Combine(CommonData, ProductName);
    public static string LegacyDataDirectory => Path.Combine(CommonData, LegacyProductName);
    public static string DatabasePath => Path.Combine(DataDirectory, "index.db");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "Logs");
    private static string MigrationMarker => Path.Combine(DataDirectory, ".migrated-from-kassensync");

    public static void EnsureDirectories()
    {
        MigrateLegacyDataIfNeeded();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static void MigrateLegacyDataIfNeeded()
    {
        if (File.Exists(MigrationMarker) || !Directory.Exists(LegacyDataDirectory))
            return;

        Directory.CreateDirectory(DataDirectory);
        CopyMissingFiles(LegacyDataDirectory, DataDirectory);
        File.WriteAllText(MigrationMarker, DateTime.UtcNow.ToString("O"));
    }

    private static void CopyMissingFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            CopyMissingFiles(directory, target);
        }
    }
}
