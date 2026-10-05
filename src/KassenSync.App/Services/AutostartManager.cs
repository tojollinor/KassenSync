using Microsoft.Win32;

namespace KassenSync.App.Services;

public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OrdnerSync";
    private const string LegacyValueName = "KassenSync";

    public static bool IsEnabled()
    {
        MigrateLegacyEntry();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Autostart-Schlüssel konnte nicht geöffnet werden.");

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Programmpfad konnte nicht bestimmt werden.");
        key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
    }

    private static void MigrateLegacyEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        var current = key.GetValue(ValueName) as string;
        var legacy = key.GetValue(LegacyValueName) as string;
        if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(legacy))
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
                key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
        }
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
