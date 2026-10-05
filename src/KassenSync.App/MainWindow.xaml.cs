using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KassenSync.App.Models;
using KassenSync.App.Services;
using KassenSync.Core.Ipc;
using KassenSync.Core.Models;
using Microsoft.Win32;

namespace KassenSync.App;

public partial class MainWindow : Window
{
    private readonly IpcClient _client = new();
    private readonly CopyStateReader _copyStateReader = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly WindowsServiceManager _serviceManager = new();

    private readonly ObservableCollection<IndexedFileRow> _rows = new();
    private readonly ObservableCollection<SyncJobRow> _jobRows = new();
    private readonly ObservableCollection<JobFilterItem> _jobFilters = new();
    private readonly HashSet<long> _selectedFileIds = new();
    private readonly Dictionary<string, PersistentMessageWindow> _usbMissingWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PersistentMessageWindow> _usbResumeWindows =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _copyStateTimer;

    private List<IndexedFile> _allFiles = new();
    private AppSettings? _settings;
    private TrayIconManager? _trayIcon;
    private CopyProgressWindow? _copyProgressWindow;
    private PersistentMessageWindow? _persistentErrorWindow;

    private bool _refreshing;
    private bool _copyPolling;
    private bool _updateCheckRunning;
    private bool _changingHeaderSelection;
    private bool _allowClose;
    private readonly bool _updatedOnLaunch;
    private readonly bool _startMinimized;
    private string? _lastCompletedOperationId;

    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = $"OrdnerSync {AppVersion.Display}";
        AboutVersionText.Text = $"Version {AppVersion.Display}";

        FilesGrid.ItemsSource = _rows;
        JobsGrid.ItemsSource = _jobRows;
        JobFilterComboBox.ItemsSource = _jobFilters;

        TargetTypeComboBox.ItemsSource = new[]
        {
            new EnumOption<SyncTargetType>("Ordner", SyncTargetType.Folder),
            new EnumOption<SyncTargetType>("USB-Stick", SyncTargetType.UsbDrive)
        };
        TargetTypeComboBox.DisplayMemberPath = nameof(EnumOption<SyncTargetType>.Name);
        TargetTypeComboBox.SelectedValuePath = nameof(EnumOption<SyncTargetType>.Value);

        SyncModeComboBox.ItemsSource = new[]
        {
            new EnumOption<SyncMode>("Einweg", SyncMode.OneWay),
            new EnumOption<SyncMode>("Bidirektional", SyncMode.Bidirectional)
        };
        SyncModeComboBox.DisplayMemberPath = nameof(EnumOption<SyncMode>.Name);
        SyncModeComboBox.SelectedValuePath = nameof(EnumOption<SyncMode>.Value);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync(silent: true);

        _copyStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _copyStateTimer.Tick += async (_, _) => await PollCopyStateAsync();

        var args = Environment.GetCommandLineArgs();
        _updatedOnLaunch = args.Any(x =>
            string.Equals(x, "--updated", StringComparison.OrdinalIgnoreCase));
        _startMinimized = args.Any(x =>
            string.Equals(x, "--autostart", StringComparison.OrdinalIgnoreCase));

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        System.Windows.Application.Current.SessionEnding += (_, _) => _allowClose = true;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _trayIcon ??= new TrayIconManager(this, ExitApplication);
        RefreshServiceStatus();

        if (_updatedOnLaunch)
            await Task.Delay(800);

        await RefreshAllAsync(silent: false);
        await PollCopyStateAsync();

        _refreshTimer.Start();
        _copyStateTimer.Start();

        if (_startMinimized)
            _trayIcon.HideWindow();

        if (_updatedOnLaunch)
        {
            var completeWindow = new UpdateCompleteWindow();
            await completeWindow.ShowForAsync(TimeSpan.FromSeconds(3));
        }
        else if (_settings?.CheckForUpdatesOnStart == true)
        {
            await CheckForUpdatesAsync(manual: false);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
            return;

        e.Cancel = true;
        _trayIcon?.HideWindow();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            _trayIcon?.HideWindow();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _copyStateTimer.Stop();

        if (_copyProgressWindow?.IsVisible == true)
            _copyProgressWindow.Close();

        ClosePersistentWindows();

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _allowClose = true;
            ClosePersistentWindows();
            _trayIcon?.Dispose();
            _trayIcon = null;
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private async Task PollCopyStateAsync()
    {
        if (_copyPolling)
            return;

        _copyPolling = true;

        try
        {
            var state = await _copyStateReader.ReadAsync();

            if (state.IsActive)
            {
                if (_copyProgressWindow is null || !_copyProgressWindow.IsLoaded)
                    _copyProgressWindow = new CopyProgressWindow();

                _copyProgressWindow.UpdateState(state);
                return;
            }

            if (_copyProgressWindow is not null)
            {
                if (_copyProgressWindow.IsVisible)
                    _copyProgressWindow.Close();

                _copyProgressWindow = null;
            }

            if (!state.Completed ||
                string.IsNullOrWhiteSpace(state.OperationId) ||
                string.Equals(
                    _lastCompletedOperationId,
                    state.OperationId,
                    StringComparison.Ordinal))
                return;

            _lastCompletedOperationId = state.OperationId;

            if (DateTime.UtcNow - state.UpdatedAtUtc > TimeSpan.FromSeconds(10))
                return;

            if (state.Success)
            {
                var resultWindow = new CopyResultWindow(state);
                await resultWindow.ShowForAsync(TimeSpan.FromSeconds(3));
            }
            else
            {
                _persistentErrorWindow?.CloseProgrammatically();

                var errorText = string.IsNullOrWhiteSpace(state.Error)
                    ? state.Message
                    : state.Error;

                var errorWindow = new PersistentMessageWindow(
                    "Kopiervorgang fehlgeschlagen",
                    errorText ?? "Unbekannter Fehler.",
                    "OK",
                    kind: PersistentMessageKind.Error);

                errorWindow.PrimaryClicked += (_, _) =>
                {
                    errorWindow.CloseProgrammatically();
                    if (ReferenceEquals(_persistentErrorWindow, errorWindow))
                        _persistentErrorWindow = null;
                };

                _persistentErrorWindow = errorWindow;
                errorWindow.Show();
                errorWindow.Activate();
            }

            await RefreshAllAsync(silent: true);
        }
        catch
        {
        }
        finally
        {
            _copyPolling = false;
        }
    }

    private async Task RefreshAllAsync(bool silent)
    {
        if (_refreshing)
            return;

        _refreshing = true;

        try
        {
            await _client.SendAsync<ServiceStatus>(IpcMessageTypes.GetStatus);

            ServiceStatusText.Text = "Dienst läuft";
            ServiceStatusText.Foreground = System.Windows.Media.Brushes.ForestGreen;
            ServiceStatusDot.Fill = System.Windows.Media.Brushes.ForestGreen;

            var files = await _client.SendAsync<List<IndexedFile>>(IpcMessageTypes.GetFiles);
            var runtimeStates = await _client.SendAsync<List<JobRuntimeState>>(
                IpcMessageTypes.GetJobRuntimeStates);

            _allFiles = files;

            var existingIds = files.Select(x => x.Id).ToHashSet();
            _selectedFileIds.IntersectWith(existingIds);

            if (_settings is null)
            {
                _settings = await _client.SendAsync<AppSettings>(IpcMessageTypes.GetSettings);
                LoadSettingsIntoUi(_settings);
            }

            ApplyRuntimeStates(runtimeStates);
            RebuildFileRows();
            RefreshJobStatuses();
            UpdateJobStatusSummary();
            UpdateUsbPrompts(runtimeStates);

            FooterStatusText.Text = $"{_allFiles.Count} Datei(en) indexiert";
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = "Dienst nicht erreichbar";
            ServiceStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            ServiceStatusDot.Fill = System.Windows.Media.Brushes.Firebrick;
            FooterStatusText.Text = ex.Message;

            if (!silent)
                System.Windows.MessageBox.Show(
                    this,
                    ex.Message,
                    "OrdnerSync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
        }
        finally
        {
            _refreshing = false;
            RefreshServiceStatus();
        }
    }

    private void LoadSettingsIntoUi(AppSettings settings)
    {
        AutostartCheckBox.IsChecked = AutostartManager.IsEnabled();
        UpdateCheckBox.IsChecked = settings.CheckForUpdatesOnStart;

        _jobRows.Clear();

        foreach (var job in settings.Jobs)
        {
            var row = new SyncJobRow(CloneJob(job));
            row.PropertyChanged += JobRow_PropertyChanged;
            _jobRows.Add(row);
        }

        if (_jobRows.Count > 0)
            JobsGrid.SelectedIndex = 0;

        RebuildJobFilters();
        UpdateJobStatusSummary();
    }

    private void JobRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SyncJobRow row &&
            e.PropertyName is nameof(SyncJobRow.Enabled)
                or nameof(SyncJobRow.SourceFolder)
                or nameof(SyncJobRow.TargetFolder)
                or nameof(SyncJobRow.TargetType))
        {
            row.RefreshStatus();
            UpdateJobStatusSummary();
        }
    }

    private void ApplyRuntimeStates(IReadOnlyCollection<JobRuntimeState> states)
    {
        var byJob = states.ToDictionary(
            x => x.JobId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in _jobRows)
        {
            byJob.TryGetValue(row.Id, out var state);
            row.UpdateRuntimeState(state);
        }
    }

    private void RefreshJobStatuses()
    {
        foreach (var row in _jobRows)
            row.RefreshStatus();
    }

    private void UpdateUsbPrompts(IReadOnlyCollection<JobRuntimeState> states)
    {
        var byJob = states.ToDictionary(
            x => x.JobId,
            StringComparer.OrdinalIgnoreCase);

        var configuredUsbJobs = _settings?.Jobs
            .Where(x => x.Enabled && x.TargetType == SyncTargetType.UsbDrive)
            .ToArray()
            ?? Array.Empty<SyncJob>();

        var activeIds = configuredUsbJobs
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stale in _usbMissingWindows.Keys
                     .Where(x => !activeIds.Contains(x))
                     .ToArray())
            CloseAndRemove(_usbMissingWindows, stale);

        foreach (var stale in _usbResumeWindows.Keys
                     .Where(x => !activeIds.Contains(x))
                     .ToArray())
            CloseAndRemove(_usbResumeWindows, stale);

        foreach (var job in configuredUsbJobs)
        {
            byJob.TryGetValue(job.Id, out var runtime);

            var hasPending = _allFiles.Any(x =>
                string.Equals(x.JobId, job.Id, StringComparison.OrdinalIgnoreCase) &&
                x.Status is FileTransferStatus.Indexed
                    or FileTransferStatus.WaitingForTarget);

            if (!hasPending)
            {
                CloseAndRemove(_usbMissingWindows, job.Id);
                CloseAndRemove(_usbResumeWindows, job.Id);
                continue;
            }

            var targetPresent = IsConfiguredTargetPresent(job);
            var waiting = runtime?.WaitingForUserConfirmation == true;
            var declined = runtime?.DeclinedForCurrentPresence == true;

            if (!targetPresent && !waiting)
            {
                CloseAndRemove(_usbResumeWindows, job.Id);
                EnsureUsbMissingWindow(job);
                continue;
            }

            CloseAndRemove(_usbMissingWindows, job.Id);

            if (targetPresent && waiting && !declined)
            {
                EnsureUsbResumeWindow(job);
            }
            else
            {
                CloseAndRemove(_usbResumeWindows, job.Id);
            }
        }
    }

    private void EnsureUsbMissingWindow(SyncJob job)
    {
        if (_usbMissingWindows.TryGetValue(job.Id, out var existing) &&
            existing.IsVisible)
            return;

        var window = new PersistentMessageWindow(
            "Kopiervorgang nicht möglich",
            "Kein Laufwerk gefunden. Bitte jetzt verbinden zum Fortfahren.",
            "Später",
            kind: PersistentMessageKind.Warning);

        window.PrimaryClicked += async (_, _) =>
        {
            try
            {
                await _client.SendAsync<JobRuntimeState>(
                    IpcMessageTypes.DeferJob,
                    new JobControlRequest(job.Id));
            }
            catch (Exception ex)
            {
                ShowPersistentError("Job konnte nicht auf Wartend gesetzt werden", ex.Message);
            }
            finally
            {
                CloseAndRemove(_usbMissingWindows, job.Id);
                await RefreshAllAsync(silent: true);
            }
        };

        _usbMissingWindows[job.Id] = window;
        window.Show();
        window.Activate();
    }

    private void EnsureUsbResumeWindow(SyncJob job)
    {
        if (_usbResumeWindows.TryGetValue(job.Id, out var existing) &&
            existing.IsVisible)
            return;

        var window = new PersistentMessageWindow(
            "Laufwerk gefunden",
            "Es gibt noch offene Kopierjobs. Jetzt durchführen?",
            "Ja",
            "Nein",
            PersistentMessageKind.Info);

        window.PrimaryClicked += async (_, _) =>
        {
            try
            {
                await _client.SendAsync<JobRuntimeState>(
                    IpcMessageTypes.ResumeJob,
                    new JobControlRequest(job.Id));
            }
            catch (Exception ex)
            {
                ShowPersistentError("Job konnte nicht fortgesetzt werden", ex.Message);
            }
            finally
            {
                CloseAndRemove(_usbResumeWindows, job.Id);
                await RefreshAllAsync(silent: true);
            }
        };

        window.SecondaryClicked += async (_, _) =>
        {
            try
            {
                await _client.SendAsync<JobRuntimeState>(
                    IpcMessageTypes.DeclineJobResume,
                    new JobControlRequest(job.Id));
            }
            catch (Exception ex)
            {
                ShowPersistentError("Wartezustand konnte nicht gespeichert werden", ex.Message);
            }
            finally
            {
                CloseAndRemove(_usbResumeWindows, job.Id);
                await RefreshAllAsync(silent: true);
            }
        };

        _usbResumeWindows[job.Id] = window;
        window.Show();
        window.Activate();
    }

    private void ShowPersistentError(string title, string message)
    {
        _persistentErrorWindow?.CloseProgrammatically();

        var window = new PersistentMessageWindow(
            title,
            message,
            "OK",
            kind: PersistentMessageKind.Error);

        window.PrimaryClicked += (_, _) =>
        {
            window.CloseProgrammatically();
            if (ReferenceEquals(_persistentErrorWindow, window))
                _persistentErrorWindow = null;
        };

        _persistentErrorWindow = window;
        window.Show();
        window.Activate();
    }

    private static bool IsConfiguredTargetPresent(SyncJob job)
    {
        if (string.IsNullOrWhiteSpace(job.TargetFolder))
            return false;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(job.TargetFolder));
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
        }
        catch
        {
            return false;
        }
    }

    private static void CloseAndRemove(
        IDictionary<string, PersistentMessageWindow> windows,
        string jobId)
    {
        if (!windows.Remove(jobId, out var window))
            return;

        if (window.IsVisible)
            window.CloseProgrammatically();
    }

    private void ClosePersistentWindows()
    {
        foreach (var window in _usbMissingWindows.Values.ToArray())
            window.CloseProgrammatically();

        foreach (var window in _usbResumeWindows.Values.ToArray())
            window.CloseProgrammatically();

        _usbMissingWindows.Clear();
        _usbResumeWindows.Clear();

        _persistentErrorWindow?.CloseProgrammatically();
        _persistentErrorWindow = null;
    }

    private void UpdateJobStatusSummary()
    {
        var active = _jobRows.Where(x => x.Enabled).ToArray();
        JobSummaryText.Text = $"{active.Length}/{_jobRows.Count} aktiv";

        if (_jobRows.Count == 0 || active.Length == 0)
        {
            JobsStatusDot.Fill = System.Windows.Media.Brushes.Gray;
        }
        else if (active.Any(x =>
                     x.StatusText is "Quelle fehlt" or "Ziel nicht verfügbar"))
        {
            JobsStatusDot.Fill = System.Windows.Media.Brushes.Firebrick;
        }
        else if (active.Any(x =>
                     x.StatusText is "Wartet auf USB" or "Wartend"))
        {
            JobsStatusDot.Fill = System.Windows.Media.Brushes.Goldenrod;
        }
        else
        {
            JobsStatusDot.Fill = System.Windows.Media.Brushes.ForestGreen;
        }

        _trayIcon?.UpdateText($"{active.Length} aktive Jobs");
    }

    private void RebuildJobFilters()
    {
        var previous = (JobFilterComboBox.SelectedItem as JobFilterItem)?.JobId;

        _jobFilters.Clear();
        _jobFilters.Add(new JobFilterItem(null, "Alle Jobs"));

        foreach (var row in _jobRows)
            _jobFilters.Add(new JobFilterItem(row.Id, row.Name));

        JobFilterComboBox.SelectedItem =
            _jobFilters.FirstOrDefault(x =>
                string.Equals(x.JobId, previous, StringComparison.OrdinalIgnoreCase))
            ?? _jobFilters.FirstOrDefault();
    }

    private void RebuildFileRows()
    {
        var knownJobs = _jobRows.ToDictionary(
            x => x.Id,
            x => x.Name,
            StringComparer.OrdinalIgnoreCase);

        var selectedFilter = JobFilterComboBox.SelectedItem as JobFilterItem;

        var files = _allFiles
            .Where(x => knownJobs.ContainsKey(x.JobId))
            .Where(x =>
                selectedFilter?.JobId is null ||
                string.Equals(
                    x.JobId,
                    selectedFilter.JobId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.DetectedAtUtc)
            .ToArray();

        _rows.Clear();

        foreach (var file in files)
        {
            var row = new IndexedFileRow
            {
                Source = file,
                JobName = knownJobs.GetValueOrDefault(file.JobId, "Unbekannter Job"),
                IsSelected = _selectedFileIds.Contains(file.Id)
            };

            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(IndexedFileRow.IsSelected))
                    return;

                if (row.IsSelected)
                    _selectedFileIds.Add(row.Id);
                else
                    _selectedFileIds.Remove(row.Id);

                UpdateSelectionText();
            };

            _rows.Add(row);
        }

        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        var selectedVisible = _rows.Count(x => x.IsSelected);
        SelectionText.Text = $"{selectedVisible} Datei(en) markiert";

        _changingHeaderSelection = true;

        SelectAllFilesCheckBox.IsChecked = _rows.Count == 0
            ? false
            : selectedVisible == 0
                ? false
                : selectedVisible == _rows.Count
                    ? true
                    : null;

        _changingHeaderSelection = false;
    }

    private void SelectAllFilesCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_changingHeaderSelection)
            return;

        foreach (var row in _rows)
            row.IsSelected = true;
    }

    private void SelectAllFilesCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_changingHeaderSelection)
            return;

        foreach (var row in _rows)
            row.IsSelected = false;
    }

    private void JobFilterComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        RebuildFileRows();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        => await RefreshAllAsync(silent: false);

    private async void RecopyButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _selectedFileIds.ToArray();

        if (selected.Length == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Bitte mindestens eine Datei markieren.",
                "OrdnerSync",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            await _client.SendAsync<object>(
                IpcMessageTypes.RecopyFiles,
                new RecopyRequest(selected));

            FooterStatusText.Text =
                $"{selected.Length} Datei(en) zur erneuten Verarbeitung eingereiht.";

            await RefreshAllAsync(silent: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Erneute Verarbeitung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteIndexedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _selectedFileIds.ToArray();

        if (selected.Length == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Bitte mindestens einen Eintrag markieren.",
                "OrdnerSync",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"{selected.Length} markierte(n) Eintrag/Einträge aus dem OrdnerSync-Index entfernen?\n\n" +
            "Die Quelldateien werden nicht gelöscht. Sind sie noch vorhanden, können sie bei einem späteren Scan erneut erkannt werden.",
            "Einträge aus Index entfernen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _client.SendAsync<object>(
                IpcMessageTypes.DeleteFiles,
                new DeleteFilesRequest(selected));

            foreach (var id in selected)
                _selectedFileIds.Remove(id);

            FooterStatusText.Text =
                $"{selected.Length} Eintrag/Einträge zum Löschen aus dem Index angefordert.";

            await RefreshAllAsync(silent: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Einträge konnten nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddJobButton_Click(object sender, RoutedEventArgs e)
    {
        var number = 1;
        var existingNames = _jobRows
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (existingNames.Contains($"Job {number}"))
            number++;

        var row = new SyncJobRow(new SyncJob
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = $"Job {number}",
            Enabled = true,
            TargetType = SyncTargetType.Folder,
            Mode = SyncMode.OneWay,
            IncludeSubdirectories = true,
            AllowedExtensions = new List<string>()
        });

        row.PropertyChanged += JobRow_PropertyChanged;
        _jobRows.Add(row);
        JobsGrid.SelectedItem = row;
        JobsGrid.ScrollIntoView(row);
        RebuildJobFilters();
        UpdateJobStatusSummary();
    }

    private void DeleteJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not SyncJobRow selected)
            return;

        if (_jobRows.Count <= 1)
        {
            System.Windows.MessageBox.Show(
                this,
                "Mindestens ein Sync-Job muss vorhanden bleiben.",
                "OrdnerSync",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            this,
            $"Job '{selected.Name}' wirklich aus der Konfiguration entfernen?",
            "Job löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _jobRows.Remove(selected);
        JobsGrid.SelectedIndex = Math.Min(
            JobsGrid.SelectedIndex,
            _jobRows.Count - 1);

        RebuildJobFilters();
        RebuildFileRows();
        UpdateJobStatusSummary();
    }

    private void BrowseJobSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not SyncJobRow job)
            return;

        var dialog = new OpenFolderDialog { Title = "Quellordner auswählen" };

        if (Directory.Exists(job.SourceFolder))
            dialog.InitialDirectory = job.SourceFolder;

        if (dialog.ShowDialog(this) == true)
            job.SourceFolder = dialog.FolderName;
    }

    private void BrowseJobTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not SyncJobRow job)
            return;

        var title = job.TargetType == SyncTargetType.UsbDrive
            ? "Ordner auf dem USB-Stick auswählen"
            : "Zielordner auswählen";

        var dialog = new OpenFolderDialog { Title = title };

        if (Directory.Exists(job.TargetFolder))
            dialog.InitialDirectory = job.TargetFolder;

        if (dialog.ShowDialog(this) == true)
            job.TargetFolder = dialog.FolderName;
    }

    private async void SaveJobsButton_Click(object sender, RoutedEventArgs e)
        => await SaveSettingsAsync();

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        => await SaveSettingsAsync();

    private async Task SaveSettingsAsync()
    {
        SaveSettingsButton.IsEnabled = false;

        try
        {
            var settings = BuildSettingsFromUi();

            _settings = await _client.SendAsync<AppSettings>(
                IpcMessageTypes.SaveSettings,
                settings);

            AutostartManager.SetEnabled(_settings.GuiAutostart);
            LoadSettingsIntoUi(_settings);
            RebuildFileRows();

            FooterStatusText.Text = "Einstellungen und Jobs gespeichert.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Einstellungen konnten nicht gespeichert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
        }
    }

    private AppSettings BuildSettingsFromUi()
    {
        var jobs = _jobRows.Select(x => x.ToModel()).ToList();

        var settings = new AppSettings
        {
            Jobs = jobs,
            GuiAutostart = AutostartCheckBox.IsChecked == true,
            CheckForUpdatesOnStart = UpdateCheckBox.IsChecked == true,
            RescanIntervalSeconds = _settings?.RescanIntervalSeconds ?? 30,
            FileStableDelayMilliseconds =
                _settings?.FileStableDelayMilliseconds ?? 1500
        };

        return settings;
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning)
            return;

        _updateCheckRunning = true;
        CheckUpdatesButton.IsEnabled = false;
        UpdateProgressWindow? progressWindow = null;

        try
        {
            FooterStatusText.Text = "Prüfe auf Updates …";

            var update = await _updateService.CheckForUpdateAsync();

            if (update is null)
            {
                FooterStatusText.Text = "OrdnerSync ist aktuell.";

                if (manual)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"OrdnerSync {AppVersion.Display} ist bereits aktuell.",
                        "OrdnerSync Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var answer = System.Windows.MessageBox.Show(
                this,
                $"OrdnerSync {update.Version} ist verfügbar.\n\nJetzt herunterladen und installieren?",
                "OrdnerSync Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
            {
                FooterStatusText.Text = $"Update {update.Version} verfügbar.";
                return;
            }

            progressWindow = new UpdateProgressWindow { Owner = this };
            progressWindow.Show();

            var progress = new Progress<int>(
                p => progressWindow.SetDownloadProgress(p));

            var setupPath = await _updateService.DownloadAndVerifyAsync(
                update,
                progress);

            progressWindow.SetInstalling();
            await Task.Delay(300);

            _updateService.LaunchInstaller(setupPath);
            progressWindow.Close();

            _allowClose = true;
            _trayIcon?.Dispose();
            _trayIcon = null;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            progressWindow?.Close();
            FooterStatusText.Text = "Update abgebrochen.";

            if (manual)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "Die Administratorabfrage wurde abgebrochen.",
                    "OrdnerSync Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            progressWindow?.Close();
            FooterStatusText.Text = "Updateprüfung fehlgeschlagen.";

            if (manual)
            {
                System.Windows.MessageBox.Show(
                    this,
                    ex.Message,
                    "OrdnerSync Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            _updateCheckRunning = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        => await CheckForUpdatesAsync(manual: true);

    private void RefreshServiceButton_Click(object sender, RoutedEventArgs e)
        => RefreshServiceStatus();

    private async void StartServiceButton_Click(object sender, RoutedEventArgs e)
        => await RunServiceActionAsync("--start-service");

    private async void StopServiceButton_Click(object sender, RoutedEventArgs e)
        => await RunServiceActionAsync("--stop-service");

    private async void RestartServiceButton_Click(object sender, RoutedEventArgs e)
        => await RunServiceActionAsync("--restart-service");

    private void RefreshServiceStatus()
    {
        try
        {
            var info = _serviceManager.GetInfo();

            ServiceInstalledText.Text =
                $"Installiert: {(info.Installed ? "Ja" : "Nein")}";

            ServiceStateText.Text = $"Status: {info.StatusText}";

            if (!info.Installed)
            {
                ServiceSettingsStatusDot.Fill = System.Windows.Media.Brushes.Firebrick;
            }
            else if (info.StatusText.Contains(
                         "Läuft",
                         StringComparison.OrdinalIgnoreCase))
            {
                ServiceSettingsStatusDot.Fill = System.Windows.Media.Brushes.ForestGreen;
            }
            else
            {
                ServiceSettingsStatusDot.Fill = System.Windows.Media.Brushes.Goldenrod;
            }
        }
        catch (Exception ex)
        {
            ServiceInstalledText.Text = "Installiert: unbekannt";
            ServiceStateText.Text = $"Status: {ex.Message}";
            ServiceSettingsStatusDot.Fill = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private async Task RunServiceActionAsync(string argument)
    {
        try
        {
            await _serviceManager.RunElevatedActionAsync(argument);
            await Task.Delay(700);
            RefreshServiceStatus();
            await RefreshAllAsync(silent: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            FooterStatusText.Text = "Dienstaktion abgebrochen.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "OrdnerSync Dienst",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            RefreshServiceStatus();
        }
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(
            "https://github.com/tojollinor/OrdnerSync")
        {
            UseShellExecute = true
        });
    }

    private static SyncJob CloneJob(SyncJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Enabled = job.Enabled,
        SourceFolder = job.SourceFolder,
        TargetFolder = job.TargetFolder,
        TargetType = job.TargetType,
        IncludeSubdirectories = job.IncludeSubdirectories,
        Mode = job.Mode,
        AllowedExtensions = job.AllowedExtensions.ToList(),
        PropagateDeletes = false
    };

    private sealed record JobFilterItem(string? JobId, string Name);
    private sealed record EnumOption<T>(string Name, T Value) where T : struct, Enum;
}
