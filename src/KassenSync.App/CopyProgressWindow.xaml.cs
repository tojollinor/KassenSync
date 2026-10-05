using System.Windows;
using KassenSync.Core.Models;

namespace KassenSync.App;

public partial class CopyProgressWindow : Window
{
    public CopyProgressWindow() => InitializeComponent();

    public void UpdateState(CopyOperationState state)
    {
        MessageText.Text = state.Message;
        CopyProgressBar.Value = Math.Clamp(state.Percent, 0, 100);
        PercentText.Text = $"{Math.Clamp(state.Percent, 0, 100)} %";
        if (!IsVisible) Show();
    }
}
