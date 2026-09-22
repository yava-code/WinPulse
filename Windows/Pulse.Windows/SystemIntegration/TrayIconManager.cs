namespace Pulse.Windows.SystemIntegration;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Pulse.Core.Services;

public sealed class TrayIconManager : IDisposable
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

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

        var contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(235, 235, 240),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Renderer = new DarkPulseMenuRenderer()
        };

        // Enable Windows 11 rounded corners on popup
        contextMenu.HandleCreated += (s, e) =>
        {
            try
            {
                int cornerVal = DWMWCP_ROUND;
                DwmSetWindowAttribute(contextMenu.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerVal, sizeof(int));
            }
            catch { }
        };

        // Title Header
        var titleItem = new ToolStripMenuItem("Pulse") { Enabled = false, Tag = "Title" };
        titleItem.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        // Toggle Panel
        var toggleItem = new ToolStripMenuItem("Показать / скрыть панель", null, (_, _) =>
        {
            _onTogglePanel();
        });
        contextMenu.Items.Add(toggleItem);

        // Settings
        var settingsItem = new ToolStripMenuItem("Настройки...", null, (_, _) =>
        {
            _onOpenSettings();
        })
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        contextMenu.Items.Add(settingsItem);

        // Refresh
        var refreshItem = new ToolStripMenuItem("Обновить квоты", null, async (_, _) =>
        {
            await _usageStore.RefreshAllAsync();
        });
        contextMenu.Items.Add(refreshItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Quit Pulse
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) =>
        {
            _onExit();
        })
        {
            Tag = "Quit"
        };
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Pulse — Монитор лимитов AI",
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
            _notifyIcon.ShowBalloonTip(3000, "Pulse", "Pulse запущен. Нажмите для открытия настроек, правый клик — меню.", ToolTipIcon.Info);
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
                g.SmoothingMode = SmoothingMode.AntiAlias;
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

    /// <summary>
    /// Custom sleek dark mode renderer for Windows 11 / macOS dark aesthetic.
    /// </summary>
    private sealed class DarkPulseMenuRenderer : ToolStripRenderer
    {
        private static readonly Color BgColor = Color.FromArgb(24, 24, 28);       // #18181C
        private static readonly Color BorderColor = Color.FromArgb(46, 46, 56);   // #2E2E38
        private static readonly Color HoverBg = Color.FromArgb(44, 44, 54);       // #2C2C36
        private static readonly Color HoverQuitBg = Color.FromArgb(70, 22, 26);   // Soft dark crimson
        private static readonly Color NormalText = Color.FromArgb(235, 235, 240);
        private static readonly Color MutedText = Color.FromArgb(135, 135, 145);
        private static readonly Color QuitText = Color.FromArgb(255, 95, 95);
        private static readonly Color SeparatorColor = Color.FromArgb(42, 42, 50);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(BgColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(BorderColor, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled) return;

            if (e.Item.Selected)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool isQuit = e.Item.Tag as string == "Quit";
                using var brush = new SolidBrush(isQuit ? HoverQuitBg : HoverBg);

                var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using var path = CreateRoundedRectangle(rect, 4);
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool isTitle = e.Item.Tag as string == "Title";
            bool isQuit = e.Item.Tag as string == "Quit";

            Color textColor;
            if (!e.Item.Enabled || isTitle)
            {
                textColor = MutedText;
            }
            else if (isQuit)
            {
                textColor = e.Item.Selected ? Color.FromArgb(255, 130, 130) : QuitText;
            }
            else
            {
                textColor = e.Item.Selected ? Color.White : NormalText;
            }

            // Left padding 14px for sleek alignment
            var textRect = new Rectangle(14, e.TextRectangle.Y, e.Item.Width - 28, e.TextRectangle.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                textRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(SeparatorColor, 1);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
