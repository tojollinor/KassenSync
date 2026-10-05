using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace KassenSync.App;

public enum PersistentMessageKind
{
    Info,
    Warning,
    Error,
    Success
}

public partial class PersistentMessageWindow : Window
{
    private bool _allowClose;

    public event EventHandler? PrimaryClicked;
    public event EventHandler? SecondaryClicked;

    public PersistentMessageWindow(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        PersistentMessageKind kind = PersistentMessageKind.Info)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        StatusDot.Fill = kind switch
        {
            PersistentMessageKind.Success => System.Windows.Media.Brushes.ForestGreen,
            PersistentMessageKind.Warning => System.Windows.Media.Brushes.Goldenrod,
            PersistentMessageKind.Error => System.Windows.Media.Brushes.Firebrick,
            _ => System.Windows.Media.Brushes.DodgerBlue
        };

        Closing += OnClosing;
    }

    public void CloseProgrammatically()
    {
        _allowClose = true;
        Close();
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        => PrimaryClicked?.Invoke(this, EventArgs.Empty);

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        => SecondaryClicked?.Invoke(this, EventArgs.Empty);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }
}
