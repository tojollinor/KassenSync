namespace KassenSync.Core.Services;

public static class AppPaths
{
    public const string ProductName = "KassenSync";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static string DatabasePath => Path.Combine(DataDirectory, "index.db");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "Logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
