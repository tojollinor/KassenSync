using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public static class AppSettingsValidator
{
    public static void Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceFolder))
            throw new InvalidOperationException("Bitte einen Quellordner angeben.");

        if (!Path.IsPathFullyQualified(settings.SourceFolder))
            throw new InvalidOperationException("Der Quellordner muss ein vollständiger Pfad sein.");

        if (string.IsNullOrWhiteSpace(settings.TargetDrive))
            throw new InvalidOperationException("Bitte ein Ziellaufwerk angeben.");

        var drive = settings.TargetDrive.Trim();
        if (drive.Length == 2 && drive[1] == ':')
            drive += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(drive);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root) || !string.Equals(full.TrimEnd('\\','/'), root.TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das Ziellaufwerk muss z. B. als E:\\ angegeben werden.");

        if (settings.RescanIntervalSeconds < 5)
            settings.RescanIntervalSeconds = 5;
        if (settings.FileStableDelayMilliseconds < 250)
            settings.FileStableDelayMilliseconds = 250;

        settings.SourceFolder = Path.GetFullPath(settings.SourceFolder.Trim());
        settings.TargetDrive = root;
        settings.TargetSubfolder = settings.TargetSubfolder.Trim().Trim('\\','/');
        settings.AllowedExtensions = settings.AllowedExtensions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
