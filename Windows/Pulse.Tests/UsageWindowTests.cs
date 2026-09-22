namespace Pulse.Tests;

using Pulse.Core.Models;
using Xunit;

public class UsageWindowTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.001, 1)]      // Never show 0% if any usage exists
    [InlineData(0.009, 1)]
    [InlineData(0.01, 1)]
    [InlineData(0.50, 50)]
    [InlineData(0.75, 75)]
    [InlineData(0.989, 99)]     // Never show 100% until fully reached
    [InlineData(0.995, 99)]
    [InlineData(1.0, 100)]
    [InlineData(1.25, 125)]
    public void UsedPercent_FollowsDisplayRule(double fraction, int expectedPercent)
    {
        var window = new UsageWindow
        {
            Id = "test",
            Kind = WindowKind.FiveHour,
            UsedFraction = fraction
        };

        Assert.Equal(expectedPercent, window.UsedPercent);
    }

    [Fact]
    public void UsageTint_LevelsAreAccurate()
    {
        Assert.Equal(UsageLevel.Normal, UsageTint.LevelFor(0.25, isExhausted: false));
        Assert.Equal(UsageLevel.Caution, UsageTint.LevelFor(0.55, isExhausted: false));
        Assert.Equal(UsageLevel.Warning, UsageTint.LevelFor(0.85, isExhausted: false));
        Assert.Equal(UsageLevel.Spent, UsageTint.LevelFor(0.10, isExhausted: true)); // Spent always wins
    }

    [Fact]
    public void FormattedResetCountdown_FormatsCorrectly()
    {
        var now = DateTime.UtcNow;

        var windowHours = new UsageWindow
        {
            Id = "test",
            Kind = WindowKind.FiveHour,
            UsedFraction = 0.5,
            ResetsAt = now.AddHours(2).AddMinutes(15)
        };

        Assert.Contains("2h", windowHours.FormattedResetCountdown());

        var windowDays = new UsageWindow
        {
            Id = "test",
            Kind = WindowKind.Weekly,
            UsedFraction = 0.5,
            ResetsAt = now.AddDays(3).AddHours(4)
        };

        Assert.Contains("3d", windowDays.FormattedResetCountdown());
    }
}
