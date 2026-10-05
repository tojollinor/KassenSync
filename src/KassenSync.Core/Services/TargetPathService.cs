using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class TargetPathService
{
    public bool IsTargetAvailable(AppSettings settings)
    {
        var root = NormalizeTargetDrive(settings.TargetDrive);
        return Directory.Exists(root);
    }

    public string GetTargetRoot(AppSettings settings)
    {
        var driveRoot = NormalizeTargetDrive(settings.TargetDrive);
        var subfolder = NormalizeSubfolder(settings.TargetSubfolder);
        return string.IsNullOrWhiteSpace(subfolder)
            ? driveRoot
            : Path.Combine(driveRoot, subfolder);
    }

    public string GetDestinationPath(IndexedFile file, AppSettings settings)
    {
        var root = Path.GetFullPath(GetTargetRoot(settings));
        var destination = Path.GetFullPath(Path.Combine(root, file.RelativePath));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der berechnete Zielpfad liegt außerhalb des konfigurierten Zielordners.");

        return destination;
    }

    private static string NormalizeTargetDrive(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Kein Ziellaufwerk konfiguriert.");

        var trimmed = value.Trim();
        if (trimmed.Length == 2 && trimmed[1] == ':')
            trimmed += Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(trimmed);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root) || !string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das Ziellaufwerk muss als Laufwerkswurzel angegeben werden, z. B. E:\\.");

        return root;
    }

    private static string NormalizeSubfolder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim().Trim('\\', '/');
        if (Path.IsPathRooted(trimmed))
            throw new InvalidOperationException("Der Ziel-Unterordner muss relativ zum Ziellaufwerk sein.");

        var segments = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("Der Ziel-Unterordner enthält einen ungültigen Pfadabschnitt.");

        return Path.Combine(segments);
    }
}
