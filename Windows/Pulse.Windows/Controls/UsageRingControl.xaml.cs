namespace Pulse.Windows.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Pulse.Core.Models;

public partial class UsageRingControl : UserControl
{
    public static readonly DependencyProperty UsedFractionProperty =
        DependencyProperty.Register(nameof(UsedFraction), typeof(double), typeof(UsageRingControl),
            new PropertyMetadata(0.0, OnUsedFractionChanged));

    public static readonly DependencyProperty AnimatedFractionProperty =
        DependencyProperty.Register(nameof(AnimatedFraction), typeof(double), typeof(UsageRingControl),
            new PropertyMetadata(0.0, OnAnimatedFractionChanged));

    public static readonly DependencyProperty IsExhaustedProperty =
        DependencyProperty.Register(nameof(IsExhausted), typeof(bool), typeof(UsageRingControl),
            new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty ProviderNameProperty =
        DependencyProperty.Register(nameof(ProviderName), typeof(string), typeof(UsageRingControl),
            new PropertyMetadata("Provider", OnProviderNameChanged));

    public static readonly DependencyProperty PercentTextProperty =
        DependencyProperty.Register(nameof(PercentText), typeof(string), typeof(UsageRingControl),
            new PropertyMetadata("0%", OnPercentTextChanged));

    public double UsedFraction
    {
        get => (double)GetValue(UsedFractionProperty);
        set => SetValue(UsedFractionProperty, value);
    }

    public double AnimatedFraction
    {
        get => (double)GetValue(AnimatedFractionProperty);
        set => SetValue(AnimatedFractionProperty, value);
    }

    public bool IsExhausted
    {
        get => (bool)GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    public string ProviderName
    {
        get => (string)GetValue(ProviderNameProperty);
        set => SetValue(ProviderNameProperty, value);
    }

    public string PercentText
    {
        get => (string)GetValue(PercentTextProperty);
        set => SetValue(PercentTextProperty, value);
    }

    public Provider Provider { get; set; }

    public bool ShowsPercentages
    {
        get => PercentLabel.Visibility == Visibility.Visible;
        set => PercentLabel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public UsageRingControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateColors();
            UpdateArcGeometry(AnimatedFraction);
        };
        MouseEnter += OnMouseEnter;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private static void OnUsedFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control)
        {
            control.UpdateColors();
            control.AnimateToFraction((double)e.NewValue);
        }
    }

    private static void OnAnimatedFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control)
        {
            control.UpdateArcGeometry((double)e.NewValue);
        }
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control)
        {
            control.UpdateColors();
        }
    }

    public static readonly DependencyProperty AvatarColorProperty =
        DependencyProperty.Register(nameof(AvatarColor), typeof(string), typeof(UsageRingControl),
            new PropertyMetadata("#E06C53", OnAvatarColorChanged));

    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(Pulse.Core.BotMark.BotMarkMood), typeof(UsageRingControl),
            new PropertyMetadata(Pulse.Core.BotMark.BotMarkMood.Idle, OnMoodChanged));

    public static readonly DependencyProperty PersonaProperty =
        DependencyProperty.Register(nameof(Persona), typeof(Pulse.Core.BotMark.BotMarkPersona), typeof(UsageRingControl),
            new PropertyMetadata(Pulse.Core.BotMark.BotMarkPersona.Calm, OnPersonaChanged));

    public static readonly DependencyProperty LeanBiasProperty =
        DependencyProperty.Register(nameof(LeanBias), typeof(double), typeof(UsageRingControl),
            new PropertyMetadata(0.0, OnLeanBiasChanged));

    public string AvatarColor
    {
        get => (string)GetValue(AvatarColorProperty);
        set => SetValue(AvatarColorProperty, value);
    }

    public Pulse.Core.BotMark.BotMarkMood Mood
    {
        get => (Pulse.Core.BotMark.BotMarkMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public Pulse.Core.BotMark.BotMarkPersona Persona
    {
        get => (Pulse.Core.BotMark.BotMarkPersona)GetValue(PersonaProperty);
        set => SetValue(PersonaProperty, value);
    }

    public double LeanBias
    {
        get => (double)GetValue(LeanBiasProperty);
        set => SetValue(LeanBiasProperty, value);
    }

    private static void OnAvatarColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control && e.NewValue is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                control.BotMark.SetColors(color);
            }
            catch { }
        }
    }

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control && e.NewValue is Pulse.Core.BotMark.BotMarkMood mood)
        {
            control.BotMark.Mood = mood;
        }
    }

    private static void OnPersonaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control && e.NewValue is Pulse.Core.BotMark.BotMarkPersona persona)
        {
            control.BotMark.Persona = persona;
        }
    }

    private static void OnLeanBiasChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control && e.NewValue is double bias)
        {
            control.BotMark.LeanBias = bias;
        }
    }

    private static void OnProviderNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control)
        {
            var color = GetAvatarColor(control.ProviderName);
            control.AvatarColor = color;
        }
    }

    private static void OnPercentTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageRingControl control)
        {
            control.PercentLabel.Text = control.PercentText;
        }
    }

    private void AnimateToFraction(double target)
    {
        var clamped = Math.Clamp(target, 0.0, 1.0);
        var anim = new DoubleAnimation
        {
            To = clamped,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(AnimatedFractionProperty, anim);
    }

    private void UpdateColors()
    {
        var level = UsageTint.LevelFor(UsedFraction, IsExhausted);
        var hex = UsageTint.HexColorFor(level);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();

        ProgressArcPath.Stroke = brush;
        GlowEffect.Color = brush.Color;
    }

    private void UpdateArcGeometry(double fraction)
    {
        const double radius = 18.0;
        const double center = 18.0;

        if (fraction <= 0.001)
        {
            ProgressArcPath.Data = Geometry.Empty;
            return;
        }

        // Clamp just below 360 to prevent complete circle zero-length arc bug in WPF
        var angle = Math.Min(fraction * 360.0, 359.99);
        var rad = angle * (Math.PI / 180.0);

        var startPoint = new Point(center, 0);
        var endPoint = new Point(center + radius * Math.Sin(rad), center - radius * Math.Cos(rad));
        var isLargeArc = angle > 180.0;

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            Segments = new PathSegmentCollection
            {
                new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true)
            }
        };

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ProgressArcPath.Data = geometry;
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new DoubleAnimation(0.7, TimeSpan.FromMilliseconds(200));
        GlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
        ForwardPointer(e);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ForwardPointer(e);
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(300));
        GlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
        BotMark.ClearPointer();
    }

    private void ForwardPointer(System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(BotMark);
        var halfW = BotMark.ActualWidth / 2.0;
        var halfH = BotMark.ActualHeight / 2.0;
        if (halfW > 0 && halfH > 0)
        {
            BotMark.SetPointer((pos.X - halfW) / halfW, (pos.Y - halfH) / halfH);
        }
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "AI";
        if (name.Contains("Claude", StringComparison.OrdinalIgnoreCase)) return "CL";
        if (name.Contains("Codex", StringComparison.OrdinalIgnoreCase)) return "OA";
        if (name.Contains("Cursor", StringComparison.OrdinalIgnoreCase)) return "CR";
        if (name.Contains("Copilot", StringComparison.OrdinalIgnoreCase)) return "GH";
        if (name.Contains("Antigravity", StringComparison.OrdinalIgnoreCase)) return "AG";
        if (name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) return "DS";
        if (name.Contains("Kimi", StringComparison.OrdinalIgnoreCase)) return "KM";
        if (name.Contains("Grok", StringComparison.OrdinalIgnoreCase)) return "GR";
        if (name.Contains("MiniMax", StringComparison.OrdinalIgnoreCase)) return "MM";
        if (name.Contains("OpenCode", StringComparison.OrdinalIgnoreCase)) return "OC";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) return $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
        return name.Length >= 2 ? name.Substring(0, 2).ToUpperInvariant() : name.ToUpperInvariant();
    }

    public static string GetAvatarColor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "#3B82F6";
        if (name.Contains("Claude", StringComparison.OrdinalIgnoreCase)) return "#E06C53"; // Terracotta orange
        if (name.Contains("Codex", StringComparison.OrdinalIgnoreCase) || name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "#22C55E"; // Vibrant green
        if (name.Contains("Cursor", StringComparison.OrdinalIgnoreCase)) return "#818CF8"; // Lavender
        if (name.Contains("Copilot", StringComparison.OrdinalIgnoreCase)) return "#3B82F6"; // Royal blue
        if (name.Contains("Kimi", StringComparison.OrdinalIgnoreCase)) return "#F472B6"; // Coral pink
        if (name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) return "#2563EB"; // Azure blue
        if (name.Contains("Grok", StringComparison.OrdinalIgnoreCase)) return "#EC4899"; // Pink

        // Hash-based deterministic selection from the GIF palette for Antigravity accounts
        string[] gifPalette =
        [
            "#E06C53", // Terracotta orange
            "#22C55E", // Neon lime green
            "#818CF8", // Periwinkle lavender
            "#F472B6", // Coral pink
            "#3B82F6", // Royal blue
            "#10B981", // Emerald green
            "#2563EB"  // Deep azure
        ];

        var hash = Math.Abs(name.GetHashCode());
        return gifPalette[hash % gifPalette.Length];
    }
}
