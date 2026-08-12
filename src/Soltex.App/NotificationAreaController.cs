using System.Drawing;
using System.Windows.Forms;

namespace Soltex.App;

internal sealed class NotificationAreaController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Icon _icon;
    private bool _backgroundNoticeShown;
    private bool _disposed;

    internal NotificationAreaController()
    {
        _icon = LoadIcon();
        ToolStripMenuItem openItem = new("Open Soltex");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        ToolStripMenuItem exitItem = new("Exit Soltex");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = _icon,
            Text = "Soltex system workspace",
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    internal bool IsAvailable => !_disposed;

    internal bool IsVisible => !_disposed && _notifyIcon.Visible;

    internal event EventHandler? OpenRequested;

    internal event EventHandler? ExitRequested;

    internal void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.Visible = visible;
    }

    internal void ShowBackgroundNotice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_backgroundNoticeShown || !_notifyIcon.Visible)
        {
            return;
        }

        _backgroundNoticeShown = true;
        _notifyIcon.ShowBalloonTip(
            3_000,
            "Soltex is still running",
            "Performance sampling is paused while the window is hidden. Open or exit Soltex from the notification area.",
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            Icon? associated = Icon.ExtractAssociatedIcon(processPath);
            if (associated is not null)
            {
                return associated;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
