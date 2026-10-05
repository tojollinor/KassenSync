using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using KassenSync.Core.Models;

namespace KassenSync.App.Models;

public sealed class IndexedFileRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public required IndexedFile Source { get; init; }
    public string JobName { get; init; } = string.Empty;

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
        FileTransferStatus.Indexed => "Bereit",
        FileTransferStatus.WaitingForTarget => "Wartet",
        FileTransferStatus.Copying => "Wird kopiert",
        FileTransferStatus.Copied => "Fertig",
        FileTransferStatus.Failed => "Fehler",
        FileTransferStatus.SourceMissing => "Quelle fehlt",
        FileTransferStatus.Conflict => "Konflikt",
        _ => Source.Status.ToString()
    };

    public Brush StatusBrush => Source.Status switch
    {
        FileTransferStatus.Copied => Brushes.ForestGreen,
        FileTransferStatus.Indexed => Brushes.Goldenrod,
        FileTransferStatus.WaitingForTarget => Brushes.Goldenrod,
        FileTransferStatus.Copying => Brushes.DodgerBlue,
        FileTransferStatus.Failed => Brushes.Firebrick,
        FileTransferStatus.SourceMissing => Brushes.Firebrick,
        FileTransferStatus.Conflict => Brushes.Firebrick,
        _ => Brushes.Gray
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
