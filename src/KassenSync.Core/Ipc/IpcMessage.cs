using System.Text.Json;

namespace KassenSync.Core.Ipc;

public sealed class IpcRequest
{
    public string Type { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed class IpcResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public JsonElement Payload { get; set; }

    public static IpcResponse Ok<T>(T payload) => new()
    {
        Success = true,
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    public static IpcResponse Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Payload = JsonSerializer.SerializeToElement<object?>(null)
    };
}

public static class IpcMessageTypes
{
    public const string GetStatus = "get-status";
    public const string GetFiles = "get-files";
    public const string GetSettings = "get-settings";
    public const string SaveSettings = "save-settings";
    public const string RecopyFiles = "recopy-files";
    public const string DeleteFiles = "delete-files";
    public const string DeleteJob = "delete-job";
    public const string GetJobRuntimeStates = "get-job-runtime-states";
    public const string DeferJob = "defer-job";
    public const string ResumeJob = "resume-job";
    public const string DeclineJobResume = "decline-job-resume";
    public const string CopyProgress = "copy-progress";
    public const string CopyCompleted = "copy-completed";
    public const string SettingsChanged = "settings-changed";
}

public sealed record ServiceStatus(bool Running, DateTime UtcNow);
public sealed record RecopyRequest(IReadOnlyList<long> FileIds);
public sealed record DeleteFilesRequest(IReadOnlyList<long> FileIds);
public sealed record DeleteJobRequest(string JobId);
public sealed record JobControlRequest(string JobId);
