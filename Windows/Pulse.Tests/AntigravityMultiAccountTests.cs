namespace Pulse.Tests;

using Pulse.Core.Models;
using Pulse.Core.Providers;
using Pulse.Core.Platform;
using Xunit;

public class AntigravityMultiAccountTests
{
    private sealed class TestPaths : IPlatformPaths
    {
        public string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public string PulseStorageDirectory => Path.Combine(AppData, "Pulse");

        public string GetCodexAuthPath() => Path.Combine(UserProfile, ".codex", "auth.json");
        public string GetClaudeCredentialsPath() => Path.Combine(UserProfile, ".claude", "credentials.json");
        public string GetCursorStateDbPath() => Path.Combine(AppData, "Cursor", "User", "globalStorage", "state.vscdb");
        public string GetGrokAuthPath() => Path.Combine(UserProfile, ".grok", "auth.json");
        public string GetCommandCodeAuthPath() => Path.Combine(UserProfile, ".commandcode", "auth.json");
        public string GetAntigravityGoogleAccountsPath() => Path.Combine(UserProfile, ".gemini", "google_accounts.json");
        public string GetAntigravityOAuthCredsPath() => Path.Combine(UserProfile, ".gemini", "oauth_creds.json");
    }

    [Fact]
    public async Task DiscoverAccounts_FindsBothYasenvarfAndMurderinerd()
    {
        var paths = new TestPaths();
        var service = new AntigravityUsageService(paths);

        var accounts = await service.DiscoverAccountsAsync();
        Assert.NotEmpty(accounts);

        var emails = accounts.Select(a => a.Id).ToList();
        Assert.Contains(emails, e => e.Contains("yasenvarf@gmail.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(emails, e => e.Contains("murderinerd@gmail.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FetchAsync_ReturnsLiveQuotasForAccounts()
    {
        var paths = new TestPaths();
        var service = new AntigravityUsageService(paths);

        var account = new AccountKey(Provider.Antigravity, "antigravity#yasenvarf@gmail.com");
        var usage = await service.FetchAsync(account);

        Assert.Equal(UsageState.Live, usage.State);
        Assert.NotEmpty(usage.Windows);
        Assert.Null(usage.UnavailabilityReason);
    }
}
