namespace KassenSync.Core.Models;

public sealed record JobRuntimeState(
    string JobId,
    bool WaitingForUserConfirmation,
    bool DeclinedForCurrentPresence,
    bool TargetPresent,
    DateTime UpdatedAtUtc);
