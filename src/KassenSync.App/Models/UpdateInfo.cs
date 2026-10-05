namespace KassenSync.App.Models;

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string SetupDownloadUrl,
    string? ChecksumDownloadUrl,
    string? ReleaseName);
