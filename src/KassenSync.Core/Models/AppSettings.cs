namespace KassenSync.Core.Models;

public sealed class AppSettings
{
    public List<SyncJob> Jobs { get; set; } = new();

    public bool GuiAutostart { get; set; } = true;
    public bool CheckForUpdatesOnStart { get; set; } = true;
    public SuccessNotificationMode SuccessNotificationMode { get; set; } = SuccessNotificationMode.Timed;
    public int SuccessNotificationSeconds { get; set; } = 3;
    public string UiBackgroundColor { get; set; } = "#EAF6FF";
    public int RescanIntervalSeconds { get; set; } = 30;
    public int FileStableDelayMilliseconds { get; set; } = 1500;

    // Legacy-Felder bleiben bis zur vollständigen 0.3.x-Migration lesbar.
    // Sie werden automatisch mit Job 1 synchronisiert.
    public string SourceFolder { get; set; } = @"C:\OrdnerSync\Input";
    public bool IncludeSubdirectories { get; set; } = true;
    public string TargetDrive { get; set; } = @"E:\";
    public string TargetSubfolder { get; set; } = "Export";
    public List<string> AllowedExtensions { get; set; } = new() { ".pdf", ".xml", ".csv" };
}

public enum SuccessNotificationMode
{
    Off = 0,
    Timed = 1,
    Persistent = 2
}
