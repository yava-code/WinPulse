namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class GrokUsageService : IUsageProviderService
{
    public Provider Provider => Provider.Grok;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;

    private const string BillingEndpoint = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";
    private const string SettingsEndpoint = "https://cli-chat-proxy.grok.com/v1/settings";

    public GrokUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string? GetToken()
    {
        // 1. Check credential store
        var stored = _credentials?.GetKey(Provider.Grok);
        if (!string.IsNullOrWhiteSpace(stored)) return stored;

        // 2. Check ~/.grok/auth.json
        var authPath = _paths.GetGrokAuthPath();
        if (!File.Exists(authPath)) return null;

        try
        {
            var json = File.ReadAllText(authPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("token", out var tProp))
            {
                return tProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("access_token", out var atProp))
            {
                return atProp.GetString();
            }
        }
        catch { }

        return null;
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var token = GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return ProviderUsage.Unavailable(account, "Sign in to Grok CLI first (~/.grok/auth.json not found)");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BillingEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("x-xai-token-auth", "xai-grok-cli");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Grok session expired. Run 'grok' to renew.");
            }

            if (!res.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"Grok server returned HTTP {(int)res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var usage = ParseBillingJson(json, account);

            // Enrich plan name if available
            var plan = await TryFetchPlanAsync(token, cancellationToken);
            if (!string.IsNullOrEmpty(plan))
            {
                usage = usage with { Plan = plan };
            }

            return usage;
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Grok connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseBillingJson(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
        {
            var pct = config.TryGetProperty("creditUsagePercent", out var cup) && cup.ValueKind == JsonValueKind.Number
                ? cup.GetDouble()
                : 0.0;

            DateTime? resetsAt = null;
            if (config.TryGetProperty("billingPeriodEnd", out var bpe) && bpe.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(bpe.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
            {
                resetsAt = parsedDt.ToUniversalTime();
            }

            windows.Add(new UsageWindow
            {
                Id = "grok.pool",
                Kind = WindowKind.Weekly,
                Scope = "Grok Allowance",
                UsedFraction = Math.Clamp(pct / 100.0, 0.0, 1.0),
                WindowSeconds = 7 * 86400,
                ResetsAt = resetsAt,
                IsExhausted = pct >= 100
            });
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            UnavailabilityReason = windows.Count == 0 ? "No Grok limits reported" : null
        };
    }

    private async Task<string?> TryFetchPlanAsync(string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, SettingsEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("x-xai-token-auth", "xai-grok-cli");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("subscriptionTierDisplay", out var std) && std.ValueKind == JsonValueKind.String)
            {
                var val = std.GetString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }

        return null;
    }
}
