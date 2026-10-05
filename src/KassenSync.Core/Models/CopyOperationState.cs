namespace KassenSync.Core.Models;

public sealed record CopyOperationState(
    string OperationId,
    bool IsActive,
    bool Completed,
    bool Success,
    int CurrentFile,
    int TotalFiles,
    string FileName,
    int Percent,
    string Message,
    string? Error,
    DateTime UpdatedAtUtc)
{
    public static CopyOperationState Idle { get; } = new(
        string.Empty, false, false, false, 0, 0, string.Empty, 0, "Bereit", null, DateTime.UtcNow);
}
