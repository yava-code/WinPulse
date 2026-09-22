namespace Pulse.Core.Models;

public enum UsageState
{
    Live,
    Stale,
    Unavailable
}

public sealed record ProviderUsage
{
    public required AccountKey Account { get; init; }
    public required IReadOnlyList<UsageWindow> Windows { get; init; }
    public DateTime ObservedAt { get; init; } = DateTime.UtcNow;
    public UsageState State { get; init; } = UsageState.Live;
    public string? Plan { get; init; }
    public string? CreditBalance { get; init; }
    public string? UnavailabilityReason { get; init; }

    public UsageWindow? HeadlineWindow => Windows.Count == 0 ? null :
        Windows.FirstOrDefault(w => w.IsExhausted) ??
        Windows.MaxBy(w => w.UsedFraction);

    public UsageWindow? SecondWindow(UsageWindow? headline)
    {
        if (headline == null || Windows.Count <= 1) return null;

        // Prefer highest in same model group/scope, else highest overall other than headline
        var sameScope = Windows.Where(w => w.Id != headline.Id && w.Scope == headline.Scope).ToList();
        if (sameScope.Count > 0)
        {
            return sameScope.MaxBy(w => w.UsedFraction);
        }

        return Windows.Where(w => w.Id != headline.Id).MaxBy(w => w.UsedFraction);
    }

    public static ProviderUsage Unavailable(AccountKey account, string reason) => new()
    {
        Account = account,
        Windows = Array.Empty<UsageWindow>(),
        State = UsageState.Unavailable,
        UnavailabilityReason = reason
    };
}
