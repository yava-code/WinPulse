namespace Pulse.Windows.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Windows.Controls;
using Pulse.Windows.SystemIntegration;

public partial class FloatingRailWindow : Window
{
    private readonly UsageStore _usageStore;
    private readonly AppSettings _appSettings;
    private readonly Action _onOpenSettings;
    private readonly Action _onExit;

    private bool _isRightDocked = true;
    private bool _isCollapsed = false;
    private readonly DispatcherTimer _collapseTimer;
    private readonly TranslateTransform _railTranslate = new();

    private bool _isDragging = false;
    private Point _dragStartPoint;
    private double _windowStartLeft;
    private double _windowStartTop;

    public FloatingRailWindow(
        UsageStore usageStore,
        AppSettings appSettings,
        Action onOpenSettings,
        Action onExit)
    {
        InitializeComponent();
        _usageStore = usageStore;
        _appSettings = appSettings;
        _onOpenSettings = onOpenSettings;
        _onExit = onExit;

        RailContainer.RenderTransform = _railTranslate;

        // Startup grace period: keep rail expanded for 8 seconds so user sees it on launch
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(800); // Standard interval afterwards
            if (_isRightDocked && !RailContainer.IsMouseOver && !DetailPopup.IsMouseOver)
            {
                CollapseRail();
            }
        };

        // Intercept close commands to hide window rather than destroying WPF visual tree
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        Loaded += OnLoaded;
        RailContainer.MouseEnter += OnRailMouseEnter;
        RailContainer.MouseLeave += OnRailMouseLeave;

        SliverBar.MouseEnter += (_, _) =>
        {
            _collapseTimer.Stop();
            ExpandRail();
        };

        RootCanvas.MouseMove += (s, e) =>
        {
            if (_isRightDocked && _isCollapsed && !_isDragging)
            {
                var pt = e.GetPosition(RootCanvas);
                if (pt.X >= RootCanvas.ActualWidth - 68)
                {
                    _collapseTimer.Stop();
                    ExpandRail();
                }
            }
        };

        DetailPopup.MouseEnter += (_, _) => _collapseTimer.Stop();
        DetailPopup.MouseLeave += (_, _) =>
        {
            DetailPopup.Visibility = Visibility.Collapsed;
            if (_isRightDocked && !RailContainer.IsMouseOver)
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            }
        };

        RailContainer.MouseLeftButtonDown += OnRailMouseDown;
        RailContainer.MouseMove += OnRailMouseMove;
        RailContainer.MouseLeftButtonUp += OnRailMouseUp;

        RailContainer.ContextMenu = CreateRailContextMenu();
        RailContainer.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                _onOpenSettings();
                e.Handled = true;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DockToRightEdge();
        Win32WindowHelper.ApplyFloatingWindowStyles(this);
    }

    public void DockToRightEdge()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width;
        Top = Math.Max(workArea.Top + 10, workArea.Top + (workArea.Height - Height) / 2.0);
        _isRightDocked = true;
    }

    private ContextMenu CreateRailContextMenu()
    {
        var menu = new ContextMenu();

        var refreshItem = new MenuItem { Header = "Refresh Now" };
        refreshItem.Click += async (_, _) => await _usageStore.RefreshAllAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => _onOpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => _onExit();
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnRailMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        ExpandRail();
    }

    private void OnRailMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRightDocked && !_isDragging)
        {
            _collapseTimer.Stop();
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(800);
            _collapseTimer.Start();
        }
    }

    public void ExpandRailTemporarily()
    {
        ExpandRail();
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromSeconds(6);
        _collapseTimer.Start();
    }

    private void CollapseRail()
    {
        if (!_isRightDocked || !_appSettings.AutoCollapse || _isDragging) return;
        if (RailContainer.IsMouseOver || DetailPopup.IsMouseOver) return;

        _isCollapsed = true;
        DetailPopup.Visibility = Visibility.Collapsed;

        // 1. Fade out rings stack completely so NO red pixels/glow can peek past the edge!
        var fadeOutRings = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        RingsStack.BeginAnimation(OpacityProperty, fadeOutRings);

        // 2. Slide rail out to the right so only 6px sliver is visible
        var targetX = Math.Max(0, RailContainer.ActualWidth - 6);
        if (targetX <= 0) targetX = 58;
        var slideAnim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        _railTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);

        // 3. Smoothly fade IN sliver bar near the end of the slide
        SliverBar.Visibility = Visibility.Visible;
        var fadeInSliver = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SliverBar.BeginAnimation(OpacityProperty, fadeInSliver);
    }

    private void ExpandRail()
    {
        _isCollapsed = false;
        _collapseTimer.Stop();

        // 1. Slide rail back to 0
        var slideAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _railTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);

        // 2. Fade IN rings
        var fadeInRings = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        RingsStack.BeginAnimation(OpacityProperty, fadeInRings);

        // 3. Fade OUT sliver
        var fadeOutSliver = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOutSliver.Completed += (_, _) =>
        {
            if (!_isCollapsed) SliverBar.Visibility = Visibility.Collapsed;
        };
        SliverBar.BeginAnimation(OpacityProperty, fadeOutSliver);
    }

    private void OnRailMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            _windowStartLeft = Left;
            _windowStartTop = Top;
            RailContainer.CaptureMouse();
            DetailPopup.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRailMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
        {
            var current = e.GetPosition(this);
            var deltaX = current.X - _dragStartPoint.X;
            var deltaY = current.Y - _dragStartPoint.Y;

            Left += deltaX;
            Top += deltaY;
        }
    }

    private void OnRailMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            RailContainer.ReleaseMouseCapture();

            // Check if docked to right edge
            var workArea = SystemParameters.WorkArea;
            if (Left + Width > workArea.Right - 50)
            {
                Left = workArea.Right - Width;
                _isRightDocked = true;
            }
            else if (Left < workArea.Left + 50)
            {
                Left = workArea.Left;
                _isRightDocked = false;
            }
            else
            {
                _isRightDocked = false;
                if (_isCollapsed) ExpandRail();
            }
        }
    }

    public void UpdateReadings(IReadOnlyDictionary<AccountKey, ProviderUsage> readings)
    {
        Dispatcher.Invoke(() =>
        {
            UsageLevel worstLevel = UsageLevel.Normal;

            // Group all accounts by Provider so there is ONLY ONE ring per provider
            var grouped = readings
                .GroupBy(kvp => kvp.Key.Provider)
                .OrderBy(g => g.Key)
                .ToList();

            var providers = grouped.Select(g => g.Key).ToList();
            var tints = Pulse.Core.BotMark.BotMarkTint.Deal(providers);
            var index = 0;

            // Index existing rings to reuse them in-place (eliminates the startup rapid-fill bug!)
            var existingRings = RingsStack.Children.OfType<UsageRingControl>()
                .ToDictionary(r => r.Provider, r => r);

            var activeProviders = new HashSet<Provider>();

            var spacingMargin = _appSettings.RailSpacing.ToLowerInvariant() switch
            {
                "tight" => new Thickness(0, 4, 0, 4),
                "loose" => new Thickness(0, 16, 0, 16),
                _ => new Thickness(0, 10, 0, 10)
            };

            foreach (var group in grouped)
            {
                var provider = group.Key;
                activeProviders.Add(provider);
                var usages = group.Select(kvp => kvp.Value).ToList();

                // Find headline reading with worst/highest UsedFraction or exhausted across all accounts
                var headlineUsage = usages
                    .OrderByDescending(u => u.HeadlineWindow?.IsExhausted == true ? 1 : 0)
                    .ThenByDescending(u => u.HeadlineWindow?.UsedFraction ?? 0.0)
                    .FirstOrDefault();

                var headline = headlineUsage?.HeadlineWindow;
                var providerTitle = provider.DisplayName();
                var tint = index < tints.Count ? tints[index] : Pulse.Core.BotMark.BotColor.FromRgb(0x4285F4);
                var persona = Pulse.Core.BotMark.BotMarkPersonaExtensions.Automatic(index);
                index++;

                var isSpent = headline?.IsExhausted ?? false;
                var hasReading = headline != null;
                var mood = Pulse.Core.BotMark.BotMarkMood.Idle;
                if (isSpent) mood = Pulse.Core.BotMark.BotMarkMood.Spent;
                else if (!hasReading) mood = Pulse.Core.BotMark.BotMarkMood.Unavailable;

                var level = UsageTint.LevelFor(headline?.UsedFraction, headline?.IsExhausted ?? false);
                if (level > worstLevel) worstLevel = level;

                var newFraction = headline?.UsedFraction ?? 0.0;
                var avatarColor = $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}";
                var percentText = headline?.PercentText ?? "0%";

                if (existingRings.TryGetValue(provider, out var ring))
                {
                    // In-place update: smooth interpolation, no reset to 0%!
                    ring.ProviderName = providerTitle;
                    ring.AvatarColor = avatarColor;
                    ring.Mood = mood;
                    ring.Persona = persona;
                    ring.LeanBias = _isRightDocked ? -1.0 : 1.0;
                    ring.IsExhausted = isSpent;
                    ring.PercentText = percentText;
                    ring.ShowsPercentages = _appSettings.SideRailShowsPercentages;
                    ring.Margin = spacingMargin;
                    ring.Tag = usages; // updated accounts list

                    if (Math.Abs(ring.UsedFraction - newFraction) > 0.001)
                    {
                        ring.UsedFraction = newFraction;
                    }
                }
                else
                {
                    // Brand new ring for newly discovered provider
                    ring = new UsageRingControl
                    {
                        Provider = provider,
                        ProviderName = providerTitle,
                        AvatarColor = avatarColor,
                        Mood = mood,
                        Persona = persona,
                        LeanBias = _isRightDocked ? -1.0 : 1.0,
                        UsedFraction = newFraction,
                        IsExhausted = isSpent,
                        PercentText = percentText,
                        ShowsPercentages = _appSettings.SideRailShowsPercentages,
                        Margin = spacingMargin,
                        Tag = usages
                    };

                    // Wire up detail card popup
                    ring.MouseEnter += (s, _) =>
                    {
                        _collapseTimer.Stop();
                        var currentUsages = (s as FrameworkElement)?.Tag as IReadOnlyList<ProviderUsage> ?? usages;
                        DetailPopup.PopulateProvider(provider, currentUsages);
                        try
                        {
                            var ringPos = ring.TranslatePoint(new Point(0, 0), RootCanvas);
                            Canvas.SetTop(DetailPopup, Math.Clamp(ringPos.Y - 20, 10, Math.Max(10, Height - 280)));
                        }
                        catch { }
                        DetailPopup.Visibility = Visibility.Visible;
                    };

                    ring.MouseLeave += (_, _) =>
                    {
                        if (!DetailPopup.IsMouseOver)
                        {
                            DetailPopup.Visibility = Visibility.Collapsed;
                        }
                    };

                    RingsStack.Children.Add(ring);
                }
            }

            // Remove rings for decommissioned providers
            for (int i = RingsStack.Children.Count - 1; i >= 0; i--)
            {
                if (RingsStack.Children[i] is UsageRingControl r && !activeProviders.Contains(r.Provider))
                {
                    RingsStack.Children.RemoveAt(i);
                }
            }

            // Update alert tint on sliver
            var sliverColor = UsageTint.HexColorFor(worstLevel);
            SliverBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sliverColor));
        });
    }

    public void ApplySettings(AppSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            // 1. Visibility
            if (!settings.IsPanelVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }

            // 2. Size
            double railWidth = settings.PanelSize.ToLowerInvariant() switch
            {
                "small" => 52,
                "large" => 76,
                _ => 64
            };
            // Keep window width at 320 to host DetailPopup without layout clipping
            RailContainer.Width = railWidth;
            RailContainer.CornerRadius = new CornerRadius(railWidth / 2.0);

            // 3. Spacing
            var spacingMargin = settings.RailSpacing.ToLowerInvariant() switch
            {
                "tight" => new Thickness(0, 4, 0, 4),
                "loose" => new Thickness(0, 16, 0, 16),
                _ => new Thickness(0, 8, 0, 8)
            };

            foreach (var child in RingsStack.Children)
            {
                if (child is UsageRingControl ring)
                {
                    ring.ShowsPercentages = settings.SideRailShowsPercentages;
                    ring.Margin = spacingMargin;
                }
            }

            // 4. Position & Docking
            if (settings.Position.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Left;
                Top = workArea.Top + (workArea.Height - Height) / 2.0;
                _isRightDocked = false;
            }
            else if (settings.Position.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                DockToRightEdge();
            }

            // 5. Expand if collapse is turned off
            if (!settings.AutoCollapse && _isCollapsed)
            {
                ExpandRail();
            }

            // 6. Theme
            Pulse.Windows.Platform.ThemeManager.ApplyTheme(settings.Theme, settings.UsesGlass);
        });
    }
}
