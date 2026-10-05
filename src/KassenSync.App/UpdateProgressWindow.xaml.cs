using System.Windows;

namespace KassenSync.App;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow() => InitializeComponent();

    public void SetDownloadProgress(int percent)
    {
        var value = Math.Clamp(percent, 0, 100);
        StatusText.Text = "Update wird von GitHub heruntergeladen …";
        UpdateProgressBar.IsIndeterminate = false;
        UpdateProgressBar.Value = value;
        PercentText.Text = $"{value} %";
    }

    public void SetInstalling()
    {
        StatusText.Text = "Installer wird gestartet …";
        UpdateProgressBar.IsIndeterminate = true;
        PercentText.Text = string.Empty;
    }
}
