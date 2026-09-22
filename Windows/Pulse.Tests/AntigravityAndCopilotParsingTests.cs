namespace Pulse.Tests;

using Pulse.Core.Models;
using Pulse.Core.Providers;
using Xunit;

public class AntigravityAndCopilotParsingTests
{
    [Fact]
    public void ParseQuotaSummaryJson_ParsesLoopbackResponseCorrectly()
    {
        var sampleJson = """
        {
          "groups": [
            {
              "groupName": "Claude 3.5 Sonnet & Partners",
              "buckets": [
                {
                  "bucketId": "claude-5h",
                  "window": "5h",
                  "remainingFraction": 0.76,
                  "resetTime": "2026-09-22T04:30:00Z"
                },
                {
                  "bucketId": "claude-weekly",
                  "window": "week",
                  "remainingFraction": 0.90,
                  "resetTime": "2026-09-28T00:00:00Z"
                }
              ]
            },
            {
              "groupName": "Gemini 1.5 Pro Models",
              "buckets": [
                {
                  "bucketId": "gemini-5h",
                  "window": "5h",
                  "remainingFraction": 0.87,
                  "resetTime": "2026-09-22T05:00:00Z"
                }
              ]
            }
          ]
        }
        """;

        var windows = AntigravityUsageService.ParseQuotaSummaryJson(sampleJson);
        Assert.Equal(3, windows.Count);

        var claude5h = windows.First(w => w.Id == "antigravity.claude-5h");
        Assert.Equal(WindowKind.FiveHour, claude5h.Kind);
        Assert.Equal(0.24, claude5h.UsedFraction, precision: 2);
        Assert.Equal(24, claude5h.UsedPercent);
        Assert.Equal("Claude & Partners", claude5h.Scope);
        Assert.NotNull(claude5h.ResetsAt);

        var gemini5h = windows.First(w => w.Id == "antigravity.gemini-5h");
        Assert.Equal(WindowKind.FiveHour, gemini5h.Kind);
        Assert.Equal(0.13, gemini5h.UsedFraction, precision: 2);
        Assert.Equal(13, gemini5h.UsedPercent);
        Assert.Equal("Gemini 1.5 Pro", gemini5h.Scope);
    }

    [Fact]
    public void ParseQuotaSummaryJson_ParsesLiveProtobufResponseCorrectly()
    {
        var sampleJson = """
        {
          "response": {
            "groups": [
              {
                "displayName": "Gemini Models",
                "buckets": [
                  {
                    "bucketId": "gemini-weekly",
                    "window": "weekly",
                    "remainingFraction": 0.9632,
                    "resetTime": "2026-09-29T10:12:52Z"
                  },
                  {
                    "bucketId": "gemini-5h",
                    "window": "5h",
                    "remainingFraction": 0.7792,
                    "resetTime": "2026-09-22T15:12:52Z"
                  }
                ]
              },
              {
                "displayName": "Claude and GPT models",
                "buckets": [
                  {
                    "bucketId": "3p-weekly",
                    "window": "weekly",
                    "remainingFraction": 1.0,
                    "resetTime": "2026-09-29T10:39:26Z"
                  },
                  {
                    "bucketId": "3p-5h",
                    "window": "5h",
                    "remainingFraction": 1.0,
                    "resetTime": "2026-09-22T15:39:26Z"
                  }
                ]
              }
            ]
          }
        }
        """;

        var windows = AntigravityUsageService.ParseQuotaSummaryJson(sampleJson);
        Assert.Equal(4, windows.Count);

        var gemini5h = windows.First(w => w.Id == "antigravity.gemini-5h");
        Assert.Equal(WindowKind.FiveHour, gemini5h.Kind);
        Assert.Equal(0.22, gemini5h.UsedFraction, precision: 2);
        Assert.Equal("Gemini", gemini5h.Scope);

        var claudeWeekly = windows.First(w => w.Id == "antigravity.3p-weekly");
        Assert.Equal(WindowKind.Weekly, claudeWeekly.Kind);
        Assert.Equal(0.0, claudeWeekly.UsedFraction, precision: 2);
        Assert.Equal("Claude and GPT", claudeWeekly.Scope);
    }

    [Fact]
    public void ParseCloudCodeBuckets_ParsesRemoteApiResponseCorrectly()
    {
        var sampleJson = """
        {
          "buckets": [
            {
              "modelId": "gemini-2.5-flash",
              "remainingFraction": 0.98,
              "resetTime": "2026-09-29T00:00:00Z"
            }
          ]
        }
        """;

        var account = new AccountKey(Provider.Antigravity, "antigravity#goosetrol@gmail.com");
        var usage = AntigravityUsageService.ParseCloudCodeBuckets(account, sampleJson);

        Assert.Equal(UsageState.Live, usage.State);
        Assert.Equal(account, usage.Account);
        Assert.Single(usage.Windows);

        var window = usage.Windows[0];
        Assert.Equal(0.02, window.UsedFraction, precision: 2);
        Assert.Equal(2, window.UsedPercent);
        Assert.Equal("Gemini 2.5 Flash", window.Scope);
    }

    [Fact]
    public void ParseCopilotJson_ParsesInternalQuotaCorrectly()
    {
        var sampleJson = """
        {
          "copilot_plan": "individual",
          "quota_reset_date_utc": "2026-10-01T00:00:00.000Z",
          "quota_snapshots": {
            "premium_interactions": {
              "has_quota": true,
              "unlimited": false,
              "percent_remaining": 80.0,
              "overage_permitted": false
            },
            "chat": {
              "has_quota": true,
              "unlimited": false,
              "percent_remaining": 95.0,
              "overage_permitted": false
            }
          }
        }
        """;

        var account = new AccountKey(Provider.Copilot);
        var usage = CopilotUsageService.ParseCopilotJson(account, sampleJson);

        Assert.Equal(UsageState.Live, usage.State);
        Assert.Equal("Individual", usage.Plan);
        Assert.Equal(2, usage.Windows.Count);

        var premium = usage.Windows.First(w => w.Id == "copilot.premium_interactions");
        Assert.Equal(0.20, premium.UsedFraction, precision: 2);
        Assert.Equal(20, premium.UsedPercent);
        Assert.Equal("Premium requests", premium.Scope);
        Assert.Equal(WindowKind.Monthly, premium.Kind);
    }
}
