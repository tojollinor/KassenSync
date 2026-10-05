using System.Text.Json;

namespace KassenSync.Core.Ipc;

public sealed class IpcMessage
{
    public string Type { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public JsonElement Payload { get; set; }
}

public static class IpcMessageTypes
{
    public const string GetStatus = "get-status";
    public const string GetFiles = "get-files";
    public const string GetSettings = "get-settings";
    public const string SaveSettings = "save-settings";
    public const string RecopyFiles = "recopy-files";
    public const string CopyProgress = "copy-progress";
    public const string CopyCompleted = "copy-completed";
    public const string SettingsChanged = "settings-changed";
}
