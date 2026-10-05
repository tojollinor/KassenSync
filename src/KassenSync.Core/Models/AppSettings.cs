namespace KassenSync.Core.Models;

public sealed class AppSettings
{
    public string SourceFolder { get; set; } = @"C:\KassenSync\Input";
    public bool IncludeSubdirectories { get; set; } = true;
    public string TargetDrive { get; set; } = @"E:\";
    public string TargetSubfolder { get; set; } = "Export";
    public List<string> AllowedExtensions { get; set; } = new() { ".pdf", ".xml", ".csv" };
    public bool GuiAutostart { get; set; } = true;
    public bool CheckForUpdatesOnStart { get; set; } = true;
    public int RescanIntervalSeconds { get; set; } = 30;
    public int FileStableDelayMilliseconds { get; set; } = 1500;
}
