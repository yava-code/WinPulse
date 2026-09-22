namespace Pulse.Windows.SystemIntegration;

using System.Drawing;
using System.Windows.Forms;
using Pulse.Core.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly UsageStore _usageStore;
    private readonly Action _onOpenSettings;
    private readonly Action _onTogglePanel;
    private readonly Action _onExit;

    public TrayIconManager(
        UsageStore usageStore,
        Action onOpenSettings,
        Action onTogglePanel,
        Action onExit)
    {
        _usageStore = usageStore;
        _onOpenSettings = onOpenSettings;
        _onTogglePanel = onTogglePanel;
        _onExit = onExit;

        var contextMenu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem("Pulse") { Enabled = false };
        titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        var toggleItem = new ToolStripMenuItem("Show / Hide Panel", null, (_, _) =>
        {
            _onTogglePanel();
        });
        contextMenu.Items.Add(toggleItem);

        var settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) =>
        {
            _onOpenSettings();
        });
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        contextMenu.Items.Add(settingsItem);

        var refreshItem = new ToolStripMenuItem("Refresh Quotas", null, async (_, _) =>
        {
            await _usageStore.RefreshAllAsync();
        });
        contextMenu.Items.Add(refreshItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Quit Pulse", null, (_, _) =>
        {
            _onExit();
        });
        exitItem.ForeColor = Color.FromArgb(220, 50, 50);
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Pulse - AI Quota Monitor",
            ContextMenuStrip = contextMenu
        };

        // CRITICAL: Set Icon BEFORE setting Visible = true to ensure Windows Shell accepts NIM_ADD
        _notifyIcon.Icon = CreateDefaultPulseIcon();
        _notifyIcon.Visible = true;

        // Left click opens/activates settings, double click toggles rail
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _onOpenSettings(); } catch { }
            }
        };

        _notifyIcon.DoubleClick += (_, _) =>
        {
            try { _onTogglePanel(); } catch { }
        };
    }

    public void ShowStartupNotification()
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, "Pulse", "Pulse is active. Click icon to open settings, right-click for options.", ToolTipIcon.Info);
        }
        catch { }
    }

    private static Icon CreateDefaultPulseIcon()
    {
        var size = SystemInformation.SmallIconSize;

        // 1. Try embedded WPF assembly resource stream
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app_icon.ico", UriKind.Absolute);
            var sri = System.Windows.Application.GetResourceStream(uri);
            if (sri?.Stream != null)
            {
                using var ms = new System.IO.MemoryStream();
                sri.Stream.CopyTo(ms);
                ms.Position = 0;
                return new Icon(ms, size.Width, size.Height);
            }
        }
        catch { }

        // 2. Try file from application directory
        try
        {
            var resPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.ico");
            if (System.IO.File.Exists(resPath))
            {
                return new Icon(resPath, size.Width, size.Height);
            }
        }
        catch { }

        // 3. Try extracting icon from executable module
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                var ico = Icon.ExtractAssociatedIcon(exePath);
                if (ico != null) return ico;
            }
        }
        catch { }

        // 4. Safe procedural GDI+ fallback
        try
        {
            using var bitmap = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var bgBrush = new SolidBrush(Color.FromArgb(240, 20, 20, 24));
                g.FillEllipse(bgBrush, 1, 1, size.Width - 2, size.Height - 2);

                using var pen = new Pen(Color.FromArgb(255, 48, 209, 88), 2.5f);
                g.DrawArc(pen, 3, 3, size.Width - 6, size.Height - 6, -90, 260);

                using var dotBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
                int dotSize = Math.Max(4, size.Width / 5);
                int offset = (size.Width - dotSize) / 2;
                g.FillEllipse(dotBrush, offset, offset, dotSize, dotSize);
            }

            var hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
        }
        catch { }
    }
}
