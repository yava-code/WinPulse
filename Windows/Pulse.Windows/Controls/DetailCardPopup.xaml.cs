namespace Pulse.Windows.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pulse.Core.Models;

public partial class DetailCardPopup : UserControl
{
    public DetailCardPopup()
    {
        InitializeComponent();
    }

    private static readonly SolidColorBrush RedBorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
    private static readonly SolidColorBrush RedBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#EF44441A")!;
    private static readonly SolidColorBrush RedFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#F87171")!;
    private static readonly SolidColorBrush GreenBorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")!;
    private static readonly SolidColorBrush GreenBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E1A")!;
    private static readonly SolidColorBrush GreenFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#4ADE80")!;
    private static readonly SolidColorBrush GrayFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#A1A1AA")!;
    private static readonly SolidColorBrush LightFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#D4D4D8")!;
    private static readonly SolidColorBrush WhiteFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FAFAFA")!;
    private static readonly SolidColorBrush DarkTrackBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#27272A")!;
    private static readonly SolidColorBrush MutedFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#71717A")!;

    static DetailCardPopup()
    {
        RedBorderBrush.Freeze();
        RedBgBrush.Freeze();
        RedFgBrush.Freeze();
        GreenBorderBrush.Freeze();
        GreenBgBrush.Freeze();
        GreenFgBrush.Freeze();
        GrayFgBrush.Freeze();
        LightFgBrush.Freeze();
        WhiteFgBrush.Freeze();
        DarkTrackBrush.Freeze();
        MutedFgBrush.Freeze();
    }

    public void Populate(ProviderUsage usage)
    {
        PopulateProvider(usage.Account.Provider, [usage]);
    }

    public void PopulateProvider(Provider provider, IReadOnlyList<ProviderUsage> usages)
    {
        TitleText.Text = provider.DisplayName();
        ProviderIconPath.Data = Pulse.Windows.Platform.ProviderIcons.GetGeometry(provider);

        var anyLive = usages.Any(u => u.State == UsageState.Live);
        var primaryPlan = usages.FirstOrDefault(u => !string.IsNullOrEmpty(u.Plan))?.Plan ?? "Standard Quota";
        PlanText.Text = primaryPlan;

        if (!anyLive)
        {
            StatusBadge.BorderBrush = RedBorderBrush;
            StatusBadge.Background = RedBgBrush;
            StatusText.Foreground = RedFgBrush;
            StatusText.Text = "Offline";
        }
        else
        {
            StatusBadge.BorderBrush = GreenBorderBrush;
            StatusBadge.Background = GreenBgBrush;
            StatusText.Foreground = GreenFgBrush;
            StatusText.Text = "Live";
        }

        WindowsContainer.Children.Clear();

        if (usages.Count == 0 || usages.All(u => u.Windows.Count == 0))
        {
            var noLimits = new TextBlock
            {
                Text = usages.FirstOrDefault()?.UnavailabilityReason ?? "No active limit windows",
                FontSize = 11,
                Foreground = GrayFgBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            };
            WindowsContainer.Children.Add(noLimits);
        }
        else
        {
            for (int i = 0; i < usages.Count; i++)
            {
                var usage = usages[i];
                if (i > 0)
                {
                    // Separator between multiple accounts
                    WindowsContainer.Children.Add(new Separator
                    {
                        Background = DarkTrackBrush,
                        Margin = new Thickness(0, 6, 0, 6)
                    });
                }

                var accountEmail = usage.Account.Id.Contains('#') ? usage.Account.Id.Split('#', 2)[1] : "";
                if (!string.IsNullOrEmpty(accountEmail) || usages.Count > 1)
                {
                    var accountHeader = new TextBlock
                    {
                        Text = !string.IsNullOrEmpty(accountEmail) ? accountEmail : "Primary Account",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightFgBrush,
                        Margin = new Thickness(0, 2, 0, 4)
                    };
                    WindowsContainer.Children.Add(accountHeader);
                }

                if (usage.Windows.Count == 0)
                {
                    var noWin = new TextBlock
                    {
                        Text = usage.UnavailabilityReason ?? "No limits reported",
                        FontSize = 10,
                        Foreground = MutedFgBrush,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    WindowsContainer.Children.Add(noWin);
                }
                else
                {
                    // For Antigravity, only show headline limit on latest top model (Gemini 3.8 / Pro)
                    // to prevent half-meter tall popups when monitoring multiple accounts!
                    List<UsageWindow> windowsToShow;
                    if (provider == Provider.Antigravity && usage.Windows.Count > 2)
                    {
                        var topTier = usage.Windows
                            .Where(w => !string.IsNullOrEmpty(w.Scope) && (
                                        w.Scope.Contains("3.8", StringComparison.OrdinalIgnoreCase) ||
                                        w.Scope.Contains("Gemini 3", StringComparison.OrdinalIgnoreCase) ||
                                        w.Scope.Contains("Gemini Pro", StringComparison.OrdinalIgnoreCase) ||
                                        w.Scope.Equals("Gemini", StringComparison.OrdinalIgnoreCase)))
                            .Take(2)
                            .ToList();

                        if (topTier.Count == 0)
                        {
                            topTier = usage.Windows.Take(2).ToList();
                        }
                        windowsToShow = topTier;
                    }
                    else
                    {
                        windowsToShow = usage.Windows.ToList();
                    }

                    foreach (var win in windowsToShow)
                    {
                        var row = CreateRow(win);
                        WindowsContainer.Children.Add(row);
                    }
                }
            }
        }

        var credit = usages.FirstOrDefault(u => !string.IsNullOrEmpty(u.CreditBalance))?.CreditBalance;
        if (!string.IsNullOrEmpty(credit))
        {
            CreditSection.Visibility = Visibility.Visible;
            CreditText.Text = credit;
        }
        else
        {
            CreditSection.Visibility = Visibility.Collapsed;
        }
    }

    private static UIElement CreateRow(UsageWindow window)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        // Name & Percentage
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameLabel = new TextBlock
        {
            Text = window.DisplayName,
            FontSize = 11,
            Foreground = LightFgBrush,
            FontWeight = FontWeights.Medium
        };
        Grid.SetColumn(nameLabel, 0);
        header.Children.Add(nameLabel);

        var pctLabel = new TextBlock
        {
            Text = window.PercentText,
            FontSize = 11,
            Foreground = WhiteFgBrush,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(pctLabel, 1);
        header.Children.Add(pctLabel);
        panel.Children.Add(header);

        // Progress bar track
        var barTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = DarkTrackBrush,
            Margin = new Thickness(0, 4, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var level = UsageTint.LevelFor(window.UsedFraction, window.IsExhausted);
        var hex = UsageTint.HexColorFor(level);
        var fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        fillBrush.Freeze();

        var barFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
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

        // Reset countdown if available
        var resetStr = window.FormattedResetCountdown();
        if (!string.IsNullOrEmpty(resetStr))
        {
            var resetText = new TextBlock
            {
                Text = resetStr,
                FontSize = 10,
                Foreground = MutedFgBrush,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(resetText);
        }

        return panel;
    }
}
