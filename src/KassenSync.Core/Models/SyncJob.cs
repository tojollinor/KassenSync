namespace KassenSync.Core.Models;

public sealed class SyncJob
{
    public const string LegacyJobId = "00000000-0000-0000-0000-000000000001";

    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = "Neuer Job";
    public bool Enabled { get; set; } = true;
    public string SourceFolder { get; set; } = string.Empty;
    public string TargetFolder { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; } = true;
    public SyncMode Mode { get; set; } = SyncMode.OneWay;
    public List<string> AllowedExtensions { get; set; } = new();
    public bool PropagateDeletes { get; set; } = false;
}

public enum SyncMode
{
    OneWay = 0,
    Bidirectional = 1
}

public enum SyncSide
{
    A = 0,
    B = 1
}
