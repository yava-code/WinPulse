namespace Pulse.Core.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Core.Models;

public static class UsageReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GenerateJson(IReadOnlyDictionary<AccountKey, ProviderUsage> readings)
    {
        var now = DateTime.UtcNow;
        var accountsList = new List<object>();

        foreach (var (account, usage) in readings)
        {
            var headline = usage.HeadlineWindow;
            object? headlineObj = headline == null ? null : new
            {
                windowId = headline.Id,
                usedPercent = headline.UsedPercent,
                exhausted = headline.IsExhausted,
                resetsAt = headline.ResetsAt?.ToString("o")
            };

            var windowsList = usage.Windows.Select(w => new
            {
                id = w.Id,
                kind = w.Kind.ToString().ToLowerInvariant(),
                scope = w.Scope,
                usedPercent = w.UsedPercent,
                usedFraction = w.UsedFraction,
                exhausted = w.IsExhausted,
                windowSeconds = w.WindowSeconds,
                reportsLength = w.ReportsLength,
                estimated = w.Estimated,
                estimatedFrom = w.EstimatedFrom,
                resetsAt = w.ResetsAt?.ToString("o")
            }).ToList();

            var ageSeconds = (int)Math.Max(0, (now - usage.ObservedAt).TotalSeconds);

            accountsList.Add(new
            {
                id = account.Id,
                provider = account.Provider.Id(),
                name = account.Provider.DisplayName(),
                label = account.Provider.DisplayName(),
                plan = usage.Plan,
                creditBalance = usage.CreditBalance,
                observedAt = usage.ObservedAt.ToString("o"),
                ageSeconds = ageSeconds,
                headline = headlineObj,
                windows = windowsList
            });
        }

        var report = new
        {
            generatedAt = now.ToString("o"),
            accounts = accountsList
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
