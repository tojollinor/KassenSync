namespace KassenSync.Core.Models;

public sealed class IndexedFile
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullSourcePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public DateTime? LastCopiedAtUtc { get; set; }
    public int CopyCount { get; set; }
    public FileTransferStatus Status { get; set; } = FileTransferStatus.Indexed;
    public string? LastError { get; set; }
}

public enum FileTransferStatus
{
    Indexed = 0,
    WaitingForTarget = 1,
    Copying = 2,
    Copied = 3,
    Failed = 4,
    SourceMissing = 5
}
