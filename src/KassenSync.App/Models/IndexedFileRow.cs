using System.ComponentModel;
using System.Runtime.CompilerServices;
using KassenSync.Core.Models;

namespace KassenSync.App.Models;

public sealed class IndexedFileRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public required IndexedFile Source { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public long Id => Source.Id;
    public string FileName => Source.FileName;
    public string RelativePath => Source.RelativePath;
    public string Size => FormatBytes(Source.SizeBytes);
    public string DetectedAt => Source.DetectedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string LastCopiedAt => Source.LastCopiedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "-";
    public int CopyCount => Source.CopyCount;
    public string Status => Source.Status switch
    {
        FileTransferStatus.Indexed => "Bereit zur Ausgabe",
        FileTransferStatus.WaitingForTarget => "Wartet auf Zielmedium",
        FileTransferStatus.Copying => "Wird kopiert",
        FileTransferStatus.Copied => "Ausgegeben",
        FileTransferStatus.Failed => "Fehler",
        FileTransferStatus.SourceMissing => "Quelldatei fehlt",
        _ => Source.Status.ToString()
    };
    public string Error => Source.LastError ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? $"{bytes} B" : $"{value:0.##} {units[index]}";
    }
}
