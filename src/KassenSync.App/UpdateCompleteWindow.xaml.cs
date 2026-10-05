using System.Windows;
using KassenSync.App.Services;

namespace KassenSync.App;

public partial class UpdateCompleteWindow : Window
{
    public UpdateCompleteWindow()
    {
        InitializeComponent();
        VersionText.Text = $"OrdnerSync {AppVersion.Display} ist jetzt installiert.";
    }

    public async Task ShowForAsync(TimeSpan duration)
    {
        Show();
        await Task.Delay(duration);
        if (IsVisible) Close();
    }
}
