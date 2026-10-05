using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ObservableCollection<IndexedFileRow> _rows = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _copyStateTimer;
    private AppSettings? _settings;
    private bool _refreshing;
    private bool _copyPolling;
    private CopyProgressWindow? _copyProgressWindow;
    private string? _lastCompletedOperationId;

    public MainWindow()
    {
        InitializeComponent();
        FilesGrid.ItemsSource = _rows;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync(silent: true);

        _copyStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _copyStateTimer.Tick += async (_, _) => await PollCopyStateAsync();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _copyStateTimer.Stop();
            if (_copyProgressWindow?.IsVisible == true) _copyProgressWindow.Close();
        };

        if (Environment.GetCommandLineArgs().Any(x => string.Equals(x, "--autostart", StringComparison.OrdinalIgnoreCase)))
            WindowState = WindowState.Minimized;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateDrives();
        await RefreshAllAsync(silent: false);
        await PollCopyStateAsync();
        _refreshTimer.Start();
        _copyStateTimer.Start();
    }

    private async Task PollCopyStateAsync()
    {
        if (_copyPolling) return;
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
                if (_copyProgressWindow.IsVisible) _copyProgressWindow.Close();
                _copyProgressWindow = null;
            }

            if (!state.Completed || string.IsNullOrWhiteSpace(state.OperationId) ||
                string.Equals(_lastCompletedOperationId, state.OperationId, StringComparison.Ordinal))
                return;

            _lastCompletedOperationId = state.OperationId;
            if (DateTime.UtcNow - state.UpdatedAtUtc > TimeSpan.FromSeconds(10))
                return;

            var resultWindow = new CopyResultWindow(state);
            await resultWindow.ShowForAsync(TimeSpan.FromSeconds(3));
            await RefreshAllAsync(silent: true);
        }
        catch
        {
            // Fortschrittsanzeige darf die Haupt-GUI niemals beeinträchtigen.
        }
        finally
        {
            _copyPolling = false;
        }
    }

    private async Task RefreshAllAsync(bool silent)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            await _client.SendAsync<ServiceStatus>(IpcMessageTypes.GetStatus);
            ServiceStatusText.Text = "Dienststatus: ✓ verbunden";
            ServiceStatusText.Foreground = System.Windows.Media.Brushes.ForestGreen;

            var files = await _client.SendAsync<List<IndexedFile>>(IpcMessageTypes.GetFiles);
            var selectedIds = FilesGrid.SelectedItems.Cast<IndexedFileRow>().Select(x => x.Id).ToHashSet();
            _rows.Clear();
            foreach (var file in files) _rows.Add(new IndexedFileRow { Source = file });
            foreach (var row in _rows.Where(x => selectedIds.Contains(x.Id))) FilesGrid.SelectedItems.Add(row);

            if (_settings is null)
            {
                _settings = await _client.SendAsync<AppSettings>(IpcMessageTypes.GetSettings);
                ApplySettingsToUi(_settings);
            }

            TargetSummaryText.Text = BuildTargetSummary(_settings);
            FooterStatusText.Text = $"{_rows.Count} Datei(en) indexiert";
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = "Dienststatus: ✗ nicht erreichbar";
            ServiceStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            FooterStatusText.Text = ex.Message;
            if (!silent)
                MessageBox.Show(this, ex.Message, "KassenSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        SourceFolderTextBox.Text = settings.SourceFolder;
        IncludeSubdirectoriesCheckBox.IsChecked = settings.IncludeSubdirectories;
        TargetDriveComboBox.Text = settings.TargetDrive;
        TargetSubfolderTextBox.Text = settings.TargetSubfolder;
        ExtensionsTextBox.Text = string.Join(", ", settings.AllowedExtensions);
        AutostartCheckBox.IsChecked = AutostartManager.IsEnabled();
        UpdateCheckBox.IsChecked = settings.CheckForUpdatesOnStart;
    }

    private AppSettings ReadSettingsFromUi() => new()
    {
        SourceFolder = SourceFolderTextBox.Text.Trim(),
        IncludeSubdirectories = IncludeSubdirectoriesCheckBox.IsChecked == true,
        TargetDrive = TargetDriveComboBox.Text.Trim(),
        TargetSubfolder = TargetSubfolderTextBox.Text.Trim(),
        AllowedExtensions = ExtensionsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        GuiAutostart = AutostartCheckBox.IsChecked == true,
        CheckForUpdatesOnStart = UpdateCheckBox.IsChecked == true,
        RescanIntervalSeconds = _settings?.RescanIntervalSeconds ?? 30,
        FileStableDelayMilliseconds = _settings?.FileStableDelayMilliseconds ?? 1500
    };

    private void PopulateDrives()
    {
        var current = TargetDriveComboBox.Text;
        TargetDriveComboBox.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(x => x.Name)) TargetDriveComboBox.Items.Add(drive.Name);
        if (!string.IsNullOrWhiteSpace(current)) TargetDriveComboBox.Text = current;
    }

    private static string BuildTargetSummary(AppSettings? settings)
    {
        if (settings is null) return "-";
        return string.IsNullOrWhiteSpace(settings.TargetSubfolder) ? settings.TargetDrive : Path.Combine(settings.TargetDrive, settings.TargetSubfolder);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateDrives();
        await RefreshAllAsync(silent: false);
    }

    private async void RecopyButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = FilesGrid.SelectedItems.Cast<IndexedFileRow>().Select(x => x.Id).Distinct().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Bitte mindestens eine Datei markieren.", "KassenSync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _client.SendAsync<object>(IpcMessageTypes.RecopyFiles, new RecopyRequest(selected));
            FooterStatusText.Text = $"{selected.Length} Datei(en) zur erneuten Ausgabe eingereiht.";
            await RefreshAllAsync(silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erneute Ausgabe fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e) => FilesGrid.SelectAll();
    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectionText.Text = $"{FilesGrid.SelectedItems.Count} Datei(en) markiert";

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Quellordner auswählen" };
        if (Directory.Exists(SourceFolderTextBox.Text)) dialog.InitialDirectory = SourceFolderTextBox.Text;
        if (dialog.ShowDialog(this) == true) SourceFolderTextBox.Text = dialog.FolderName;
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsButton.IsEnabled = false;
        try
        {
            var settings = ReadSettingsFromUi();
            _settings = await _client.SendAsync<AppSettings>(IpcMessageTypes.SaveSettings, settings);
            AutostartManager.SetEnabled(_settings.GuiAutostart);
            ApplySettingsToUi(_settings);
            TargetSummaryText.Text = BuildTargetSummary(_settings);
            FooterStatusText.Text = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Einstellungen konnten nicht gespeichert werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
        }
    }
}
