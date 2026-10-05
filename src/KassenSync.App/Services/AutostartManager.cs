using Microsoft.Win32;

namespace KassenSync.App.Services;

public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KassenSync";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Autostart-Schlüssel konnte nicht geöffnet werden.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Programmpfad konnte nicht bestimmt werden.");
        key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
    }
}
