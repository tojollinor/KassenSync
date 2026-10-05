using KassenSync.Core.Models;

namespace KassenSync.App.Models;

public sealed class IndexedFileRow
{
    public required IndexedFile Source { get; init; }
    public long Id => Source.Id;
    public string FileName => Source.FileName;
    public string RelativePath => Source.RelativePath;
    public string Size => FormatBytes(Source.SizeBytes);
    public string DetectedAt => Source.DetectedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string LastCopiedAt => Source.LastCopiedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "-";
    public int CopyCount => Source.CopyCount;
    public string Status => Source.Status switch
    {
        FileTransferStatus.Indexed => "Bereit zur Ausgabe",
        FileTransferStatus.WaitingForTarget => "Wartet auf Zielmedium",
        FileTransferStatus.Copying => "Wird kopiert",
        FileTransferStatus.Copied => "Ausgegeben",
        FileTransferStatus.Failed => "Fehler",
        FileTransferStatus.SourceMissing => "Quelldatei fehlt",
        _ => Source.Status.ToString()
    };
    public string Error => Source.LastError ?? string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? $"{bytes} B" : $"{value:0.##} {units[index]}";
    }
}
