using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class TargetPathService
{
    public bool IsTargetAvailable(SyncJob job, SyncSide sourceSide)
    {
        var targetRoot = GetTargetRoot(job, sourceSide);
        var pathRoot = Path.GetPathRoot(targetRoot);

        if (string.IsNullOrWhiteSpace(pathRoot))
            return false;

        return Directory.Exists(pathRoot);
    }

    public string GetTargetRoot(SyncJob job, SyncSide sourceSide)
    {
        var configured = sourceSide == SyncSide.A
            ? job.TargetFolder
            : job.SourceFolder;

        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                $"Beim Job '{job.Name}' ist kein Zielordner konfiguriert.");

        return Path.GetFullPath(configured.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string GetDestinationPath(IndexedFile file, SyncJob job)
    {
        var root = GetTargetRoot(job, file.SourceSide);
        var destination = Path.GetFullPath(Path.Combine(root, file.RelativePath));
        var rootWithSeparator =
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Der berechnete Zielpfad liegt außerhalb des konfigurierten Zielordners.");

        return destination;
    }

    public bool IsTargetAvailable(AppSettings settings)
    {
        SettingsMigration.Normalize(settings);
        return IsTargetAvailable(settings.Jobs.First(), SyncSide.A);
    }

    public string GetTargetRoot(AppSettings settings)
    {
        SettingsMigration.Normalize(settings);
        return GetTargetRoot(settings.Jobs.First(), SyncSide.A);
    }

    public string GetDestinationPath(IndexedFile file, AppSettings settings)
    {
        SettingsMigration.Normalize(settings);
        return GetDestinationPath(file, settings.Jobs.First());
    }
}
