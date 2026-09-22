namespace Pulse.Tests;

using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Xunit;

public class UsageReportTests
{
    [Fact]
    public void GenerateJson_MatchesContract()
    {
        var now = DateTime.UtcNow;
        var readings = new Dictionary<AccountKey, ProviderUsage>
        {
            [new AccountKey(Provider.Codex)] = new ProviderUsage
            {
                Account = new AccountKey(Provider.Codex),
                Windows = new List<UsageWindow>
                {
                    new()
                    {
                        Id = "codex.primary",
                        Kind = WindowKind.FiveHour,
                        UsedFraction = 0.45,
                        WindowSeconds = 18000,
                        ResetsAt = now.AddHours(2)
                    }
                },
                Plan = "Plus",
                State = UsageState.Live
            }
        };

        var json = UsageReport.GenerateJson(readings);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("generatedAt", out _));
        Assert.True(root.TryGetProperty("accounts", out var accounts));
        Assert.Equal(JsonValueKind.Array, accounts.ValueKind);
        Assert.Equal(1, accounts.GetArrayLength());

        var first = accounts[0];
        Assert.Equal("codex", first.GetProperty("id").GetString());
        Assert.Equal("codex", first.GetProperty("provider").GetString());
        Assert.Equal("Codex", first.GetProperty("name").GetString());
        Assert.Equal("Plus", first.GetProperty("plan").GetString());

        var headline = first.GetProperty("headline");
        Assert.Equal("codex.primary", headline.GetProperty("windowId").GetString());
        Assert.Equal(45, headline.GetProperty("usedPercent").GetInt32());
    }
}
