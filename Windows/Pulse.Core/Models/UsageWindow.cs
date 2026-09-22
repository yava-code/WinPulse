namespace Pulse.Core.Models;

public enum WindowKind
{
    FiveHour,
    Daily,
    Weekly,
    Monthly,
    Spend,
    Balance,
    Messages,
    Other
}

public sealed record UsageWindow
{
    public required string Id { get; init; }
    public required WindowKind Kind { get; init; }
    public string? Scope { get; init; }
    public required double UsedFraction { get; init; }
    public int WindowSeconds { get; init; }
    public DateTime? ResetsAt { get; init; }
    public bool IsExhausted { get; init; }
    public bool ReportsLength { get; init; } = true;
    public bool Estimated { get; init; }
    public string? EstimatedFrom { get; init; }

    public int UsedPercent
    {
        get
        {
            if (UsedFraction <= 0) return 0;
            if (UsedFraction > 0 && UsedFraction < 0.01) return 1;
            if (UsedFraction >= 0.99 && UsedFraction < 1.0) return 99;
            return (int)Math.Round(UsedFraction * 100, MidpointRounding.AwayFromZero);
        }
    }

    public string PercentText => $"{UsedPercent}%";

    public string FormattedResetCountdown()
    {
        if (ResetsAt == null) return string.Empty;
        if (UsedFraction <= 0) return "100% available";

        var diff = ResetsAt.Value - DateTime.UtcNow;
        if (WindowSeconds > 0 && diff.TotalSeconds > WindowSeconds)
        {
            diff = TimeSpan.FromSeconds(WindowSeconds);
        }

        if (diff.TotalSeconds <= 0) return "resets now";

        var localResets = ResetsAt.Value.ToLocalTime();
        var timeStr = localResets.Date == DateTime.Today
            ? localResets.ToString("HH:mm")
            : localResets.ToString("MMM d, HH:mm");

        string cdStr;
        if (diff.TotalDays >= 1)
        {
            var days = (int)diff.TotalDays;
            var hours = diff.Hours;
            cdStr = hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }
        else if (diff.TotalHours >= 1)
        {
            var hours = (int)diff.TotalHours;
            var minutes = diff.Minutes;
            cdStr = minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }
        else
        {
            var m = Math.Max(1, (int)diff.TotalMinutes);
            cdStr = $"{m}m";
        }

        return $"Resets {timeStr} ({cdStr})";
    }

    public string DisplayName
    {
        get
        {
            var baseName = Kind switch
            {
                WindowKind.FiveHour => "5-hour limit",
                WindowKind.Daily => "Daily limit",
                WindowKind.Weekly => "Weekly limit",
                WindowKind.Monthly => "Monthly limit",
                WindowKind.Spend => "Spend limit",
                WindowKind.Balance => "Credit balance",
                WindowKind.Messages => "Message limit",
                _ => "Usage limit"
            };

            return string.IsNullOrEmpty(Scope) ? baseName : $"{baseName} · {Scope}";
        }
    }
}
