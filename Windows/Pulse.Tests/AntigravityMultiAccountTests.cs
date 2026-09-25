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
    public async Task DiscoverAccounts_ReturnsNonEmptyAntigravityAccounts()
    {
        var paths = new TestPaths();
        var service = new AntigravityUsageService(paths);

        var accounts = await service.DiscoverAccountsAsync();
        Assert.NotEmpty(accounts);
        Assert.All(accounts, account =>
        {
            Assert.Equal(Provider.Antigravity, account.Provider);
            Assert.False(string.IsNullOrWhiteSpace(account.Id));
        });
    }

    [Fact]
    public async Task FetchAsync_UnknownAccountWithoutSession_ReturnsUnavailable()
    {
        var paths = new TestPaths();
        var service = new AntigravityUsageService(paths);

        var account = new AccountKey(Provider.Antigravity, $"antigravity#ci-{Guid.NewGuid():N}@invalid.example");
        var usage = await service.FetchAsync(account);

        Assert.Equal(account, usage.Account);
        Assert.Equal(UsageState.Unavailable, usage.State);
        Assert.Empty(usage.Windows);
        Assert.False(string.IsNullOrWhiteSpace(usage.UnavailabilityReason));
    }
}
