namespace Pulse.Tests;

using Pulse.Core.Models;
using Pulse.Core.Providers;
using Xunit;

public class CursorParsingTests
{
    [Fact]
    public void ParseUsageSummary_ParsesPoolsCorrectly()
    {
        var json = """
        {
          "membershipType": "pro",
          "billingCycleEnd": "2026-10-15T00:00:00.000Z",
          "individualUsage": {
            "plan": {
              "enabled": true,
              "used": 1200,
              "limit": 2000,
              "remaining": 800,
              "autoPercentUsed": 45.5,
              "apiPercentUsed": 20.0
            },
            "onDemand": {
              "enabled": false,
              "used": 0,
              "limit": 0
            }
          }
        }
        """;

        var account = new AccountKey(Provider.Cursor);
        var usage = CursorUsageService.ParseUsageResponse(json, account);

        Assert.Equal(UsageState.Live, usage.State);
        Assert.Equal("Pro", usage.Plan);
        Assert.Equal(2, usage.Windows.Count);

        var auto = usage.Windows.First(w => w.Id == "cursor.models");
        Assert.Equal("Cursor Models", auto.Scope);
        Assert.Equal(0.455, auto.UsedFraction, precision: 3);
        Assert.Equal(46, auto.UsedPercent);

        var api = usage.Windows.First(w => w.Id == "cursor.other");
        Assert.Equal("Other Models", api.Scope);
        Assert.Equal(0.20, api.UsedFraction, precision: 2);
    }

    [Fact]
    public void ParseUsage_HandlesNullValuesWithoutThrowing()
    {
        // This reproduces the exact error reported by user:
        // "gpt-4" with null maxRequestUsage or null numRequests
        var json = """
        {
          "gpt-4": {
            "numRequests": null,
            "maxRequestUsage": null
          },
          "startOfMonth": "2026-09-01T00:00:00.000Z"
        }
        """;

        var account = new AccountKey(Provider.Cursor);
        var usage = CursorUsageService.ParseUsageResponse(json, account);

        Assert.NotNull(usage);
        Assert.Equal(UsageState.Unavailable, usage.State);
        Assert.Contains("No active", usage.UnavailabilityReason);
    }
}
