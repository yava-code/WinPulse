namespace Pulse.Core.Models;

public enum UsageLevel
{
    Normal,   // Green (< 50%)
    Caution,  // Amber (50% to warning threshold)
    Warning,  // Red (>= warning threshold)
    Spent     // Deep Crimson Red (Provider says isExhausted)
}

public static class UsageTint
{
    public const double DefaultCautionThreshold = 0.50;
    public const double DefaultWarningThreshold = 0.75;

    public static UsageLevel LevelFor(double? fraction, bool isExhausted, double warningThreshold = DefaultWarningThreshold)
    {
        if (isExhausted) return UsageLevel.Spent;
        if (fraction == null || fraction < DefaultCautionThreshold) return UsageLevel.Normal;
        if (fraction < warningThreshold) return UsageLevel.Caution;
        return UsageLevel.Warning;
    }

    public static string HexColorFor(UsageLevel level) => level switch
    {
        UsageLevel.Normal => "#30D158",   // iOS / macOS vibrant green
        UsageLevel.Caution => "#FF9F0A",  // vibrant amber
        UsageLevel.Warning => "#FF453A",  // vibrant red
        UsageLevel.Spent => "#D70015",    // deep crimson
        _ => "#8E8E93"                    // neutral gray
    };
}
