namespace Pulse.Tests;

using Pulse.Core.Models;
using Pulse.Core.Providers;
using Xunit;

public class CodexParsingTests
{
    [Fact]
    public void ParseUsageResponse_ValidatesCorrectly()
    {
        var sampleJson = """
        {
          "plan_type": "prolite",
          "rate_limit": {
            "limit_reached": false,
            "primary_window": {
              "used_percent": 42.5,
              "limit_window_seconds": 18000,
              "reset_at": 1726950000
            },
            "secondary_window": {
              "used_percent": 88.0,
              "limit_window_seconds": 604800,
              "reset_at": 1727500000
            }
          },
          "additional_rate_limits": [
            {
              "limit_name": "o1-mini",
              "metered_feature": "o1-mini",
              "rate_limit": {
                "limit_reached": false,
                "primary_window": {
                  "used_percent": 15.0,
                  "limit_window_seconds": 604800,
                  "reset_at": 1727500000
                }
              }
            }
          ],
          "credits": {
            "unlimited": false,
            "balance": "$25.00"
          }
        }
        """;

        var account = new AccountKey(Provider.Codex);
        var usage = CodexUsageService.ParseUsageResponse(sampleJson, account);

        Assert.Equal(UsageState.Live, usage.State);
        Assert.Equal("Pro 5x", usage.Plan);
        Assert.Equal("$25.00", usage.CreditBalance);
        Assert.Equal(3, usage.Windows.Count);

        var primary = usage.Windows.First(w => w.Id == "codex.primary_window");
        Assert.Equal(WindowKind.FiveHour, primary.Kind);
        Assert.Equal(0.425, primary.UsedFraction, precision: 3);
        Assert.Equal(43, primary.UsedPercent);
        Assert.False(primary.IsExhausted);

        var secondary = usage.Windows.First(w => w.Id == "codex.secondary_window");
        Assert.Equal(WindowKind.Weekly, secondary.Kind);
        Assert.Equal(0.88, secondary.UsedFraction, precision: 2);
        Assert.Equal(88, secondary.UsedPercent);

        var o1 = usage.Windows.First(w => w.Id == "o1-mini.primary_window");
        Assert.Equal("o1-mini", o1.Scope);
        Assert.Equal(15, o1.UsedPercent);

        // Headline should be the fullest window
        var headline = usage.HeadlineWindow;
        Assert.NotNull(headline);
        Assert.Equal("codex.secondary_window", headline.Id);
    }

    [Theory]
    [InlineData("free", "Free")]
    [InlineData("plus", "Plus")]
    [InlineData("pro", "Pro")]
    [InlineData("prolite", "Pro 5x")]
    [InlineData("team", "Team")]
    [InlineData("enterprise", "Enterprise")]
    public void MapPlanName_MapsKnownTiers(string raw, string expected)
    {
        Assert.Equal(expected, CodexUsageService.MapPlanName(raw));
    }
}
