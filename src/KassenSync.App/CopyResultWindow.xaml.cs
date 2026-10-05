using System.Windows;
using System.Windows.Media;
using KassenSync.Core.Models;

namespace KassenSync.App;

public partial class CopyResultWindow : Window
{
    public CopyResultWindow(CopyOperationState state)
    {
        InitializeComponent();
        if (state.Success)
        {
            TitleText.Text = "✓ Kopiervorgang erfolgreich";
            TitleText.Foreground = System.Windows.Media.Brushes.ForestGreen;
            MessageText.Text = state.Message;
        }
        else
        {
            TitleText.Text = "✗ Kopiervorgang fehlgeschlagen";
            TitleText.Foreground = System.Windows.Media.Brushes.Firebrick;
            MessageText.Text = string.IsNullOrWhiteSpace(state.Error) ? state.Message : state.Error;
        }
    }

    public async Task ShowForAsync(TimeSpan duration)
    {
        Show();
        await Task.Delay(duration);
        if (IsVisible) Close();
    }
}
