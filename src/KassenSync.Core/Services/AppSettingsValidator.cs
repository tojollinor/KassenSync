using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public static class AppSettingsValidator
{
    public static void Validate(AppSettings settings)
    {
        SettingsMigration.Normalize(settings);

        if (settings.Jobs.Count == 0)
            throw new InvalidOperationException("Mindestens ein Sync-Job muss vorhanden sein.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in settings.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Name))
                throw new InvalidOperationException("Jeder Sync-Job benötigt einen Namen.");

            if (!names.Add(job.Name.Trim()))
                throw new InvalidOperationException($"Der Jobname '{job.Name}' ist mehrfach vorhanden.");

            if (string.IsNullOrWhiteSpace(job.SourceFolder) || !Path.IsPathFullyQualified(job.SourceFolder))
                throw new InvalidOperationException($"Beim Job '{job.Name}' muss die Quelle ein vollständiger Ordnerpfad sein.");

            if (string.IsNullOrWhiteSpace(job.TargetFolder) || !Path.IsPathFullyQualified(job.TargetFolder))
                throw new InvalidOperationException($"Beim Job '{job.Name}' muss das Ziel ein vollständiger Ordnerpfad sein.");

            var source = Path.GetFullPath(job.SourceFolder.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.GetFullPath(job.TargetFolder.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Beim Job '{job.Name}' dürfen Quelle und Ziel nicht identisch sein.");

            if (IsNestedPath(source, target) || IsNestedPath(target, source))
                throw new InvalidOperationException(
                    $"Beim Job '{job.Name}' dürfen Quelle und Ziel nicht ineinander liegen.");

            job.Name = job.Name.Trim();
            job.SourceFolder = source;
            job.TargetFolder = target;
            job.AllowedExtensions = SettingsMigration.NormalizeExtensions(job.AllowedExtensions);
            job.PropagateDeletes = false;
        }

        SettingsMigration.Normalize(settings);
    }

    private static bool IsNestedPath(string parent, string candidate)
    {
        var normalizedParent = parent.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var normalizedCandidate = candidate.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(
            normalizedParent,
            StringComparison.OrdinalIgnoreCase);
    }
}
