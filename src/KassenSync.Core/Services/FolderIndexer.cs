using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class FolderIndexer(
    IndexDatabase database,
    FileHashService hashService,
    FileStabilityService stabilityService)
{
    public async Task<long?> TryIndexAsync(string fullPath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fullPath) || !IsAllowed(fullPath, settings.AllowedExtensions))
            return null;

        var stable = await stabilityService.WaitUntilStableAsync(fullPath, settings.FileStableDelayMilliseconds, cancellationToken);
        if (!stable || !File.Exists(fullPath))
            return null;

        var info = new FileInfo(fullPath);
        var sha256 = await hashService.ComputeSha256Async(fullPath, cancellationToken);
        if (await database.ExistsAsync(info.Name, sha256, cancellationToken))
            return null;

        var sourceRoot = Path.GetFullPath(settings.SourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(sourceRoot, full);

        var item = new IndexedFile
        {
            FileName = info.Name,
            RelativePath = relative,
            FullSourcePath = full,
            Sha256 = sha256,
            SizeBytes = info.Length,
            DetectedAtUtc = DateTime.UtcNow,
            Status = FileTransferStatus.Indexed
        };

        return await database.AddIfNewAsync(item, cancellationToken);
    }

    public async Task<int> ScanAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(settings.SourceFolder))
            return 0;

        var option = settings.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var added = 0;
        foreach (var file in Directory.EnumerateFiles(settings.SourceFolder, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryIndexAsync(file, settings, cancellationToken) is not null)
                added++;
        }
        return added;
    }

    private static bool IsAllowed(string path, IReadOnlyCollection<string> allowedExtensions)
    {
        if (allowedExtensions.Count == 0)
            return true;
        var ext = Path.GetExtension(path);
        return allowedExtensions.Any(x => string.Equals(NormalizeExtension(x), ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension)
        => extension.StartsWith('.') ? extension : "." + extension;
}
