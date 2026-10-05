using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public static class SettingsMigration
{
    public static bool Normalize(AppSettings settings)
    {
        var changed = false;

        settings.RescanIntervalSeconds = Math.Max(5, settings.RescanIntervalSeconds);
        settings.FileStableDelayMilliseconds = Math.Max(250, settings.FileStableDelayMilliseconds);
        settings.Jobs ??= new List<SyncJob>();

        if (settings.Jobs.Count == 0)
        {
            settings.Jobs.Add(new SyncJob
            {
                Id = SyncJob.LegacyJobId,
                Name = "Job 1",
                Enabled = true,
                SourceFolder = settings.SourceFolder,
                TargetFolder = BuildLegacyTarget(settings.TargetDrive, settings.TargetSubfolder),
                IncludeSubdirectories = settings.IncludeSubdirectories,
                Mode = SyncMode.OneWay,
                AllowedExtensions = NormalizeExtensions(settings.AllowedExtensions),
                PropagateDeletes = false
            });
            changed = true;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < settings.Jobs.Count; i++)
        {
            var job = settings.Jobs[i];

            if (string.IsNullOrWhiteSpace(job.Id) || !Guid.TryParse(job.Id, out _) || !seenIds.Add(job.Id))
            {
                job.Id = i == 0 ? SyncJob.LegacyJobId : Guid.NewGuid().ToString("D");
                while (!seenIds.Add(job.Id))
                    job.Id = Guid.NewGuid().ToString("D");
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(job.Name))
            {
                job.Name = $"Job {i + 1}";
                changed = true;
            }

            var extensions = NormalizeExtensions(job.AllowedExtensions);
            if (!extensions.SequenceEqual(job.AllowedExtensions, StringComparer.OrdinalIgnoreCase))
            {
                job.AllowedExtensions = extensions;
                changed = true;
            }

            // Löschweitergabe bleibt in 0.3.0 bewusst deaktiviert.
            if (job.PropagateDeletes)
            {
                job.PropagateDeletes = false;
                changed = true;
            }
        }

        if (settings.Jobs.Count > 0)
        {
            var first = settings.Jobs[0];
            var source = first.SourceFolder;
            var root = GetTargetRoot(first.TargetFolder);
            var subfolder = GetTargetSubfolder(first.TargetFolder);

            if (!string.Equals(settings.SourceFolder, source, StringComparison.OrdinalIgnoreCase) ||
                settings.IncludeSubdirectories != first.IncludeSubdirectories ||
                !string.Equals(settings.TargetDrive, root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(settings.TargetSubfolder, subfolder, StringComparison.OrdinalIgnoreCase) ||
                !settings.AllowedExtensions.SequenceEqual(first.AllowedExtensions, StringComparer.OrdinalIgnoreCase))
            {
                settings.SourceFolder = source;
                settings.IncludeSubdirectories = first.IncludeSubdirectories;
                settings.TargetDrive = root;
                settings.TargetSubfolder = subfolder;
                settings.AllowedExtensions = first.AllowedExtensions.ToList();
                changed = true;
            }
        }

        return changed;
    }

    public static List<string> NormalizeExtensions(IEnumerable<string>? extensions)
        => (extensions ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildLegacyTarget(string drive, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return string.Empty;

        var root = drive.Trim();
        return string.IsNullOrWhiteSpace(subfolder)
            ? root
            : Path.Combine(root, subfolder.Trim().Trim('\\', '/'));
    }

    private static string GetTargetRoot(string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(targetFolder))
            return string.Empty;

        try
        {
            var full = Path.GetFullPath(targetFolder);
            return Path.GetPathRoot(full) ?? full;
        }
        catch
        {
            return targetFolder;
        }
    }

    private static string GetTargetSubfolder(string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(targetFolder))
            return string.Empty;

        try
        {
            var full = Path.GetFullPath(targetFolder);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            return Path.GetRelativePath(root, full) is "." ? string.Empty : Path.GetRelativePath(root, full);
        }
        catch
        {
            return string.Empty;
        }
    }
}
