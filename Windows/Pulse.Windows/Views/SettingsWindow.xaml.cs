namespace Pulse.Windows.Views;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Core.BotMark;
using Pulse.Core.Models;
using Pulse.Core.Providers;
using Pulse.Core.Security;
using Pulse.Core.Services;
using Pulse.Windows.Controls;
using Pulse.Windows.SystemIntegration;

public partial class SettingsWindow : Window
{
    private readonly UsageStore _store;
    private readonly ICredentialStore _credentials;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly GitHubCopilotOAuthService _githubOAuthService;
    private readonly AntigravityUsageService _antigravityService;
    private readonly AppSettings _appSettings;
    private readonly Action _onSettingsSaved;

    private bool _isNavigating = false;
    private bool _isLoadingSettings = false;
    private Provider? _activeProvider = null;

    private static readonly SolidColorBrush AccentBlueBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#007AFF")!;
    private static readonly SolidColorBrush DarkTrackBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#27272A")!;
    private static readonly SolidColorBrush LightFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#D4D4D8")!;
    private static readonly SolidColorBrush WhiteFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FAFAFA")!;
    private static readonly SolidColorBrush MutedFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#71717A")!;

    static SettingsWindow()
    {
        AccentBlueBrush.Freeze();
        DarkTrackBrush.Freeze();
        LightFgBrush.Freeze();
        WhiteFgBrush.Freeze();
        MutedFgBrush.Freeze();
    }

    public SettingsWindow(
        UsageStore store,
        ICredentialStore credentials,
        GoogleOAuthService googleOAuthService,
        GitHubCopilotOAuthService githubOAuthService,
        AntigravityUsageService antigravityService,
        AppSettings appSettings,
        Action onSettingsSaved)
    {
        InitializeComponent();
        _store = store;
        _credentials = credentials;
        _googleOAuthService = googleOAuthService;
        _githubOAuthService = githubOAuthService;
        _antigravityService = antigravityService;
        _appSettings = appSettings;
        _onSettingsSaved = onSettingsSaved;

        SourceInitialized += (_, _) =>
        {
            Win32WindowHelper.EnableImmersiveDarkMode(this);
        };

        // Window drag & control buttons
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        };

        CloseWindowBtn.Click += (_, _) =>
        {
            if (!_appSettings.IsPanelVisible)
            {
                Pulse.Windows.App.ExitApplication();
            }
            else
            {
                Hide();
            }
        };
        MinWindowBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxWindowBtn.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Q && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Pulse.Windows.App.ExitApplication();
            }
        };

        Closing += (_, e) =>
        {
            if (!_appSettings.IsPanelVisible)
            {
                Pulse.Windows.App.ExitApplication();
            }
            else
            {
                e.Cancel = true;
                Hide();
            }
        };

        Loaded += (_, _) =>
        {
            LoadSettingsIntoUI();

            // Replace sidebar emojis with crisp vector icons
            if (AccountsNavList != null)
            {
                foreach (var item in AccountsNavList.Items)
                {
                    if (item is ListBoxItem lbi && lbi.Content is StackPanel sp && TryParseProvider(lbi.Tag?.ToString() ?? "", out var prov))
                    {
                        try
                        {
                            var geom = Pulse.Windows.Platform.ProviderIcons.GetGeometry(prov);
                            if (geom != null && geom != Geometry.Empty && sp.Children.Count > 0)
                            {
                                var iconPath = new Path
                                {
                                    Data = geom,
                                    Width = 14,
                                    Height = 14,
                                    Stretch = Stretch.Uniform,
                                    Fill = LightFgBrush,
                                    Margin = new Thickness(0, 0, 10, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                                sp.Children.RemoveAt(0);
                                sp.Children.Insert(0, iconPath);
                            }
                        }
                        catch { }
                    }
                }
            }
        };
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isNavigating) return;
        // Guard against premature selection events during InitializeComponent / XAML parsing
        if (PanelNavList == null || AccountsNavList == null || AppNavList == null || GeneralPaneView == null || TopHeaderTitle == null) return;
        _isNavigating = true;

        try
        {
            ListBox? activeList = sender as ListBox;
            if (activeList == null || activeList.SelectedItem == null) return;

            // Clear selections in other lists safely
            if (activeList != PanelNavList && PanelNavList != null) PanelNavList.UnselectAll();
            if (activeList != AccountsNavList && AccountsNavList != null) AccountsNavList.UnselectAll();
            if (activeList != AppNavList && AppNavList != null) AppNavList.UnselectAll();

            var selectedItem = activeList.SelectedItem as ListBoxItem;
            var tag = selectedItem?.Tag?.ToString() ?? "";

            // Hide all panes initially
            if (GeneralPaneView != null) GeneralPaneView.Visibility = Visibility.Collapsed;
            if (ProviderPaneView != null) ProviderPaneView.Visibility = Visibility.Collapsed;
            if (TokenSpendPaneView != null) TokenSpendPaneView.Visibility = Visibility.Collapsed;
            if (IntegrationsPaneView != null) IntegrationsPaneView.Visibility = Visibility.Collapsed;
            if (AboutPaneView != null) AboutPaneView.Visibility = Visibility.Collapsed;

            if (tag == "General")
            {
                _activeProvider = null;
                TopHeaderTitle.Text = "Pulse Settings";
                if (GeneralPaneView != null) GeneralPaneView.Visibility = Visibility.Visible;
            }
            else if (tag == "TokenSpend")
            {
                _activeProvider = null;
                TopHeaderTitle.Text = "Pulse Settings";
                if (TokenSpendPaneView != null) TokenSpendPaneView.Visibility = Visibility.Visible;
            }
            else if (tag == "Integrations")
            {
                _activeProvider = null;
                TopHeaderTitle.Text = "Developer integrations";
                if (IntegrationsPaneView != null) IntegrationsPaneView.Visibility = Visibility.Visible;
            }
            else if (tag == "About")
            {
                _activeProvider = null;
                TopHeaderTitle.Text = "About Pulse";
                if (AboutPaneView != null) AboutPaneView.Visibility = Visibility.Visible;
            }
            else
            {
                // Provider Account Pane
                if (TryParseProvider(tag, out var provider))
                {
                    _activeProvider = provider;
                    TopHeaderTitle.Text = "Pulse Settings";
                    if (ProviderPaneView != null) ProviderPaneView.Visibility = Visibility.Visible;
                    LoadProviderPane(provider);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating settings: {ex.Message}");
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private static bool TryParseProvider(string tag, out Provider provider)
    {
        switch (tag)
        {
            case "Antigravity": provider = Provider.Antigravity; return true;
            case "ClaudeCode": provider = Provider.ClaudeCode; return true;
            case "Codex": provider = Provider.Codex; return true;
            case "CommandCode": provider = Provider.CommandCode; return true;
            case "Cursor": provider = Provider.Cursor; return true;
            case "DeepSeek": provider = Provider.DeepSeek; return true;
            case "Devin": provider = Provider.Devin; return true;
            case "Copilot": provider = Provider.Copilot; return true;
            case "Grok": provider = Provider.Grok; return true;
            case "GrokBot": provider = Provider.GrokBot; return true;
            case "KimiCode": provider = Provider.KimiCode; return true;
            case "MiniMax": provider = Provider.MiniMax; return true;
            case "MiniMaxCN": provider = Provider.MiniMaxCN; return true;
            case "OllamaCloud": provider = Provider.OllamaCloud; return true;
            case "OpenCodeGo": provider = Provider.OpenCodeGo; return true;
            case "Volcengine": provider = Provider.Volcengine; return true;
            case "Zai": provider = Provider.Zai; return true;
            case "Zhipu": provider = Provider.GlmCoding; return true;
            default: provider = Provider.Antigravity; return false;
        }
    }

    private async void LoadProviderPane(Provider provider)
    {
        ProviderWindowsContainer.Children.Clear();

        // 1. Gather all accounts for this provider from store readings
        var readings = _store.Readings;
        var providerUsages = readings
            .Where(kvp => kvp.Key.Provider == provider)
            .Select(kvp => kvp.Value)
            .ToList();

        // If no readings in store yet, show live or placeholder
        if (providerUsages.Count == 0)
        {
            var noLimits = new TextBlock
            {
                Text = "Checking account status...",
                FontSize = 12,
                Foreground = MutedFgBrush,
                Margin = new Thickness(0, 4, 0, 8)
            };
            ProviderWindowsContainer.Children.Add(noLimits);
        }
        else
        {
            // Render limit bars for each window across all accounts of this provider
            foreach (var usage in providerUsages)
            {
                var accountEmail = usage.Account.Id.Contains('#') ? usage.Account.Id.Split('#', 2)[1] : "";
                if (!string.IsNullOrEmpty(accountEmail) && providerUsages.Count > 1)
                {
                    var accHeader = new TextBlock
                    {
                        Text = accountEmail,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightFgBrush,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    ProviderWindowsContainer.Children.Add(accHeader);
                }

                if (usage.Windows.Count == 0)
                {
                    var noWin = new TextBlock
                    {
                        Text = usage.UnavailabilityReason ?? "No active limit windows reported",
                        FontSize = 11,
                        Foreground = MutedFgBrush,
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    ProviderWindowsContainer.Children.Add(noWin);
                }
                else
                {
                    foreach (var win in usage.Windows)
                    {
                        ProviderWindowsContainer.Children.Add(CreateLimitBarRow(win));
                    }
                }
            }
        }

        // Plan & Credit
        var primaryUsage = providerUsages.FirstOrDefault();
        var planName = primaryUsage?.Plan ?? (provider == Provider.Antigravity ? "Pro" : "Standard");
        ProviderPlanText.Text = planName;

        var credit = providerUsages.FirstOrDefault(u => !string.IsNullOrEmpty(u.CreditBalance))?.CreditBalance;
        if (!string.IsNullOrEmpty(credit))
        {
            ProviderCreditRow.Visibility = Visibility.Visible;
            ProviderCreditText.Text = credit;
        }
        else
        {
            ProviderCreditRow.Visibility = Visibility.Collapsed;
        }

        // Last Read
        if (primaryUsage != null && primaryUsage.ObservedAt > DateTime.MinValue)
        {
            var elapsed = DateTime.UtcNow - primaryUsage.ObservedAt;
            if (elapsed.TotalSeconds < 60)
            {
                ProviderLastReadText.Text = $"{(int)elapsed.TotalSeconds} sec ago";
            }
            else
            {
                ProviderLastReadText.Text = $"{(int)elapsed.TotalMinutes} min, {elapsed.Seconds} sec";
            }
        }
        else
        {
            ProviderLastReadText.Text = "Just now";
        }

        // Multi-accounts list for Antigravity or connected providers
        if (provider == Provider.Antigravity)
        {
            await RefreshAntigravityAccountsListAsync();
        }
        else
        {
            MultiAccountsContainer.Visibility = Visibility.Collapsed;
        }

        // Estimated value: only shown when valid ledger extrapolation exists (1:1 with macOS Pulse)
        EstimatedValueSection.Visibility = Visibility.Collapsed;

        // Usage history: show clean macOS-style "No history yet" empty card
        NoHistoryCard.Visibility = Visibility.Visible;
        LiveHistoryCard.Visibility = Visibility.Collapsed;
    }

    private static UIElement CreateLimitBarRow(UsageWindow window)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameLabel = new TextBlock
        {
            Text = window.DisplayName,
            FontSize = 12,
            Foreground = LightFgBrush,
            FontWeight = FontWeights.Medium
        };
        Grid.SetColumn(nameLabel, 0);
        header.Children.Add(nameLabel);

        var pctLabel = new TextBlock
        {
            Text = window.PercentText,
            FontSize = 12,
            Foreground = WhiteFgBrush,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(pctLabel, 1);
        header.Children.Add(pctLabel);
        panel.Children.Add(header);

        // Progress bar track with dynamic proportional fill (100% fills full width!)
        var barTrack = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = DarkTrackBrush,
            Margin = new Thickness(0, 4, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var level = UsageTint.LevelFor(window.UsedFraction, window.IsExhausted);
        var hex = UsageTint.HexColorFor(level);
        var fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        fillBrush.Freeze();

        var barFill = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        barTrack.Child = barFill;

        barTrack.SizeChanged += (s, e) =>
        {
            if (e.NewSize.Width > 0)
            {
                barFill.Width = Math.Max(2, Math.Clamp(window.UsedFraction, 0.0, 1.0) * e.NewSize.Width);
            }
        };
        panel.Children.Add(barTrack);

        // Reset countdown
        var resetStr = window.FormattedResetCountdown();
        if (!string.IsNullOrEmpty(resetStr))
        {
            var resetText = new TextBlock
            {
                Text = resetStr,
                FontSize = 10.5,
                Foreground = MutedFgBrush,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(resetText);
        }

        return panel;
    }

    private async Task RefreshAntigravityAccountsListAsync()
    {
        MultiAccountsList.Children.Clear();

        try
        {
            var accounts = await _antigravityService.DiscoverAccountsAsync();
            if (accounts.Count > 1)
            {
                MultiAccountsContainer.Visibility = Visibility.Visible;
                foreach (var acc in accounts)
                {
                    var email = acc.Id.Contains('#') ? acc.Id.Split('#', 2)[1] : "Active Session";
                    var row = CreateAccountDisconnectRow(email);
                    MultiAccountsList.Children.Add(row);
                }
            }
            else
            {
                MultiAccountsContainer.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            MultiAccountsContainer.Visibility = Visibility.Collapsed;
        }
    }

    private Border CreateAccountDisconnectRow(string email)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = email,
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = LightFgBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var disconnectBtn = new Button
        {
            Content = "Disconnect",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x1C, 0x1C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand
        };
        disconnectBtn.Click += async (_, _) =>
        {
            _credentials.SetAccountToken($"antigravity#{email}", null);
            _credentials.SetAccountToken($"antigravity#{email}:refresh", null);
            var current = _store.GetMonitoredAccounts().Where(a => !a.Id.Contains(email, StringComparison.OrdinalIgnoreCase)).ToList();
            _store.SetMonitoredAccounts(current);
            await RefreshAntigravityAccountsListAsync();
            _onSettingsSaved();
        };
        Grid.SetColumn(disconnectBtn, 1);
        grid.Children.Add(disconnectBtn);

        border.Child = grid;
        return border;
    }

    private async void ProviderSignInBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProvider == Provider.Antigravity)
        {
            ProviderSignInBtn.IsEnabled = false;
            try
            {
                var email = await _googleOAuthService.StartOAuthFlowAsync();
                if (!string.IsNullOrEmpty(email))
                {
                    await _store.RefreshAllAsync();
                    LoadProviderPane(Provider.Antigravity);
                    _onSettingsSaved();
                }
            }
            finally
            {
                ProviderSignInBtn.IsEnabled = true;
            }
        }
        else if (_activeProvider == Provider.Copilot)
        {
            ProviderSignInBtn.IsEnabled = false;
            try
            {
                var success = await _githubOAuthService.StartDeviceFlowAsync(code => { });
                if (success)
                {
                    await _store.RefreshAllAsync();
                    LoadProviderPane(Provider.Copilot);
                    _onSettingsSaved();
                }
            }
            finally
            {
                ProviderSignInBtn.IsEnabled = true;
            }
        }
        else
        {
            // For other providers, open provider login page
            string url = _activeProvider switch
            {
                Provider.ClaudeCode => "https://claude.ai/settings",
                Provider.Codex => "https://chatgpt.com/#settings",
                Provider.Cursor => "https://www.cursor.com/settings",
                Provider.DeepSeek => "https://platform.deepseek.com",
                Provider.Grok or Provider.GrokBot => "https://x.ai",
                _ => "https://github.com/qunqin24/Pulse"
            };

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private async void ProviderRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        ProviderRefreshBtn.IsEnabled = false;
        try
        {
            await _store.RefreshAllAsync();
            if (_activeProvider.HasValue)
            {
                LoadProviderPane(_activeProvider.Value);
            }
        }
        finally
        {
            ProviderRefreshBtn.IsEnabled = true;
        }
    }

    private void SidebarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SidebarSearchBox.Text.Trim();
        foreach (var item in AccountsNavList.Items)
        {
            if (item is ListBoxItem lbi)
            {
                if (string.IsNullOrEmpty(query))
                {
                    lbi.Visibility = Visibility.Visible;
                }
                else
                {
                    var tag = lbi.Tag?.ToString() ?? "";
                    var match = tag.Contains(query, StringComparison.OrdinalIgnoreCase);
                    lbi.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void LoadSettingsIntoUI()
    {
        _isLoadingSettings = true;
        try
        {
            ShowFloatingPanelSwitch.IsChecked = _appSettings.IsPanelVisible;
            HideInFullScreenSwitch.IsChecked = _appSettings.HideInFullScreen;

            SizeSmallRadio.IsChecked = _appSettings.PanelSize.Equals("small", StringComparison.OrdinalIgnoreCase);
            SizeStandardRadio.IsChecked = _appSettings.PanelSize.Equals("standard", StringComparison.OrdinalIgnoreCase);
            SizeLargeRadio.IsChecked = _appSettings.PanelSize.Equals("large", StringComparison.OrdinalIgnoreCase);

            SpacingTightRadio.IsChecked = _appSettings.RailSpacing.Equals("tight", StringComparison.OrdinalIgnoreCase);
            SpacingStandardRadio.IsChecked = _appSettings.RailSpacing.Equals("standard", StringComparison.OrdinalIgnoreCase);
            SpacingLooseRadio.IsChecked = _appSettings.RailSpacing.Equals("loose", StringComparison.OrdinalIgnoreCase);

            LiquidGlassSwitch.IsChecked = _appSettings.UsesGlass;
            HideUntilPointedAtSwitch.IsChecked = _appSettings.AutoCollapse;

            PosLeftRadio.IsChecked = _appSettings.Position.Equals("left", StringComparison.OrdinalIgnoreCase);
            PosTopRadio.IsChecked = _appSettings.Position.Equals("top", StringComparison.OrdinalIgnoreCase);
            PosFreeRadio.IsChecked = _appSettings.Position.Equals("free", StringComparison.OrdinalIgnoreCase);
            PosRightRadio.IsChecked = _appSettings.Position.Equals("right", StringComparison.OrdinalIgnoreCase);

            PercentagesAtSideSwitch.IsChecked = _appSettings.SideRailShowsPercentages;

            ThemeDarkRadio.IsChecked = _appSettings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase);
            ThemeLightRadio.IsChecked = _appSettings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase);
            ThemePurpleRadio.IsChecked = _appSettings.Theme.Equals("purple", StringComparison.OrdinalIgnoreCase);
            ThemeGlassRadio.IsChecked = _appSettings.Theme.Equals("glass", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveSettingsField(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || _appSettings == null) return;

        _appSettings.IsPanelVisible = ShowFloatingPanelSwitch.IsChecked ?? true;
        _appSettings.HideInFullScreen = HideInFullScreenSwitch.IsChecked ?? true;

        if (SizeSmallRadio.IsChecked == true) _appSettings.PanelSize = "small";
        else if (SizeLargeRadio.IsChecked == true) _appSettings.PanelSize = "large";
        else _appSettings.PanelSize = "standard";

        if (SpacingTightRadio.IsChecked == true) _appSettings.RailSpacing = "tight";
        else if (SpacingLooseRadio.IsChecked == true) _appSettings.RailSpacing = "loose";
        else _appSettings.RailSpacing = "standard";

        _appSettings.UsesGlass = LiquidGlassSwitch.IsChecked ?? false;
        _appSettings.AutoCollapse = HideUntilPointedAtSwitch.IsChecked ?? false;

        if (PosLeftRadio.IsChecked == true) _appSettings.Position = "left";
        else if (PosTopRadio.IsChecked == true) _appSettings.Position = "top";
        else if (PosFreeRadio.IsChecked == true) _appSettings.Position = "free";
        else _appSettings.Position = "right";

        _appSettings.SideRailShowsPercentages = PercentagesAtSideSwitch.IsChecked ?? true;

        if (ThemeLightRadio.IsChecked == true) _appSettings.Theme = "light";
        else if (ThemePurpleRadio.IsChecked == true) _appSettings.Theme = "purple";
        else if (ThemeGlassRadio.IsChecked == true) _appSettings.Theme = "glass";
        else _appSettings.Theme = "dark";

        // Apply theme immediately
        Pulse.Windows.Platform.ThemeManager.ApplyTheme(_appSettings.Theme, _appSettings.UsesGlass);

        _appSettings.Save();
        _onSettingsSaved();
    }

    private void InstallHooksBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Status line hooks installed to Claude Code & Codex configuration.", "Pulse Integrations", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SidebarQuitBtn_Click(object sender, RoutedEventArgs e)
    {
        Pulse.Windows.App.ExitApplication();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_appSettings.IsPanelVisible)
        {
            Pulse.Windows.App.ExitApplication();
        }
        else
        {
            e.Cancel = true;
            Hide();
        }
    }
}
