namespace KassenSync.Core.Models;

public sealed record CopyProgress(
    long IndexedFileId,
    string FileName,
    int CurrentFile,
    int TotalFiles,
    long BytesCopied,
    long TotalBytes,
    int Percent,
    string State,
    string? Error = null);
