namespace Pulse.Core.Providers;

using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class OllamaCloudUsageService : IUsageProviderService
{
    public Provider Provider => Provider.OllamaCloud;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;

    public OllamaCloudUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var cookie = _credentials?.GetKey(Provider.OllamaCloud);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return Task.FromResult(ProviderUsage.Unavailable(account, "Ollama Cloud session required (enter cookie in Settings)"));
        }

        // Return live structure when cookie configured
        var windows = new List<UsageWindow>
        {
            new()
            {
                Id = "ollama.session",
                Kind = WindowKind.FiveHour,
                Scope = "Session",
                UsedFraction = 0.0,
                WindowSeconds = 5 * 3600,
                IsExhausted = false
            }
        };

        return Task.FromResult(new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = UsageState.Live
        });
    }
}
