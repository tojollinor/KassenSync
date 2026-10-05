using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using KassenSync.Core.Models;

namespace KassenSync.App.Models;

public sealed class SyncJobRow : INotifyPropertyChanged
{
    private readonly SyncJob _job;
    private bool _waitingForUserConfirmation;
    private bool? _runtimeTargetPresent;

    public SyncJobRow(SyncJob job)
    {
        _job = job;
    }

    public string Id => _job.Id;

    public bool Enabled
    {
        get => _job.Enabled;
        set
        {
            if (_job.Enabled == value) return;
            _job.Enabled = value;
            OnPropertyChanged();
            RefreshStatus();
        }
    }

    public string Name
    {
        get => _job.Name;
        set
        {
            if (_job.Name == value) return;
            _job.Name = value;
            OnPropertyChanged();
        }
    }

    public string SourceFolder
    {
        get => _job.SourceFolder;
        set
        {
            if (_job.SourceFolder == value) return;
            _job.SourceFolder = value;
            OnPropertyChanged();
            RefreshStatus();
        }
    }

    public string TargetFolder
    {
        get => _job.TargetFolder;
        set
        {
            if (_job.TargetFolder == value) return;
            _job.TargetFolder = value;
            OnPropertyChanged();
            RefreshStatus();
        }
    }

    public SyncTargetType TargetType
    {
        get => _job.TargetType;
        set
        {
            if (_job.TargetType == value) return;
            _job.TargetType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetTypeText));
            RefreshStatus();
        }
    }

    public string TargetTypeText => TargetType == SyncTargetType.UsbDrive ? "USB-Stick" : "Ordner";

    public bool IncludeSubdirectories
    {
        get => _job.IncludeSubdirectories;
        set
        {
            if (_job.IncludeSubdirectories == value) return;
            _job.IncludeSubdirectories = value;
            OnPropertyChanged();
        }
    }

    public SyncMode Mode
    {
        get => _job.Mode;
        set
        {
            if (_job.Mode == value) return;
            _job.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeText));
        }
    }

    public string ModeText => Mode == SyncMode.Bidirectional ? "Bidirektional (Beta)" : "Einweg";

    public string AllowedExtensionsText
    {
        get => string.Join(", ", _job.AllowedExtensions);
        set
        {
            var normalized = (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.StartsWith('.') ? x : "." + x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_job.AllowedExtensions.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
                return;

            _job.AllowedExtensions = normalized;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get
        {
            if (!Enabled) return "Inaktiv";
            if (string.IsNullOrWhiteSpace(SourceFolder) || !Directory.Exists(SourceFolder))
                return "Quelle fehlt";

            if (TargetType == SyncTargetType.UsbDrive)
            {
                if (_waitingForUserConfirmation)
                    return "Wartend";

                if (_runtimeTargetPresent == false || !IsTargetRootAvailable())
                    return "Wartet auf USB";
            }

            if (!IsTargetRootAvailable())
                return "Ziel nicht verfügbar";

            return "Bereit";
        }
    }

    public System.Windows.Media.Brush StatusBrush => StatusText switch
    {
        "Bereit" => System.Windows.Media.Brushes.ForestGreen,
        "Inaktiv" => System.Windows.Media.Brushes.Gray,
        "Wartet auf USB" => System.Windows.Media.Brushes.Goldenrod,
        "Wartend" => System.Windows.Media.Brushes.Goldenrod,
        _ => System.Windows.Media.Brushes.Firebrick
    };

    public SyncJob ToModel() => new()
    {
        Id = _job.Id,
        Name = _job.Name,
        Enabled = _job.Enabled,
        SourceFolder = _job.SourceFolder,
        TargetFolder = _job.TargetFolder,
        TargetType = _job.TargetType,
        IncludeSubdirectories = _job.IncludeSubdirectories,
        Mode = _job.Mode,
        AllowedExtensions = _job.AllowedExtensions.ToList(),
        PropagateDeletes = false
    };

    public void UpdateRuntimeState(JobRuntimeState? state)
    {
        _waitingForUserConfirmation = state?.WaitingForUserConfirmation == true;
        _runtimeTargetPresent = state?.TargetPresent;
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    private bool IsTargetRootAvailable()
    {
        if (string.IsNullOrWhiteSpace(TargetFolder))
            return false;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(TargetFolder));
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
        }
        catch
        {
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
