using System.Drawing;
using System.Windows;

namespace KassenSync.App.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Window _window;
    private readonly Action _exitAction;

    public TrayIconManager(Window window, Action exitAction)
    {
        _window = window;
        _exitAction = exitAction;

        var icon = Environment.ProcessPath is { Length: > 0 } exe
            ? Icon.ExtractAssociatedIcon(exe)
            : SystemIcons.Application;

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "OrdnerSync",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("OrdnerSync öffnen", null, (_, _) => ShowWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => _exitAction());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.ShowInTaskbar = true;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        });
    }

    public void HideWindow()
    {
        _window.ShowInTaskbar = false;
        _window.Hide();
    }

    public void UpdateText(string text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "OrdnerSync" : $"OrdnerSync - {text}";
        _notifyIcon.Text = value.Length <= 63 ? value : value[..63];
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
