namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;

public sealed class ClaudeCodeUsageService : IUsageProviderService
{
    public Provider Provider => Provider.ClaudeCode;

    private readonly IPlatformPaths _paths;
    private readonly HttpClient _httpClient;
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    public ClaudeCodeUsageService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    private string? LoadAccessToken()
    {
        var credsPath = _paths.GetClaudeCredentialsPath();
        if (!File.Exists(credsPath)) return null;

        try
        {
            var json = File.ReadAllText(credsPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (!oauth.TryGetProperty("accessToken", out var tokenProp)) return null;

            if (oauth.TryGetProperty("expiresAt", out var expProp))
            {
                var expiresAtMs = expProp.GetInt64();
                var expiryDate = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs).UtcDateTime;
                if (DateTime.UtcNow > expiryDate)
                {
                    // Expired
                    return null;
                }
            }

            return tokenProp.GetString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var token = LoadAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return ProviderUsage.Unavailable(account, "Sign in to Claude Code CLI first (missing or expired .credentials.json)");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("User-Agent", "Pulse-Windows/1.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Claude Code credentials expired. Re-authenticate via 'claude login'.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"Claude API returned HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsageResponse(content, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Claude connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageResponse(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();

        // Claude returns five_hour and seven_day (or weekly)
        if (root.TryGetProperty("five_hour", out var fh))
        {
            if (TryParseWindow(fh, "claude.five_hour", WindowKind.FiveHour, scope: null, out var w))
            {
                windows.Add(w);
            }
        }

        if (root.TryGetProperty("seven_day", out var sd))
        {
            if (TryParseWindow(sd, "claude.weekly", WindowKind.Weekly, scope: null, out var w))
            {
                windows.Add(w);
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            UnavailabilityReason = windows.Count == 0 ? "No quota limits reported by Claude" : null
        };
    }

    private static bool TryParseWindow(JsonElement element, string id, WindowKind kind, string? scope, out UsageWindow window)
    {
        window = null!;
        if (!element.TryGetProperty("used_percent", out var pct)) return false;

        var percent = pct.GetDouble();
        int seconds = element.TryGetProperty("limit_window_seconds", out var sec) ? sec.GetInt32() : (kind == WindowKind.FiveHour ? 18000 : 604800);
        DateTime? resetsAt = element.TryGetProperty("reset_at", out var rst)
            ? DateTimeOffset.FromUnixTimeSeconds((long)rst.GetDouble()).UtcDateTime
            : null;

        bool isSpent = element.TryGetProperty("is_exhausted", out var exh) && exh.GetBoolean();

        window = new UsageWindow
        {
            Id = id,
            Kind = kind,
            Scope = scope,
            UsedFraction = percent / 100.0,
            WindowSeconds = seconds,
            ResetsAt = resetsAt,
            IsExhausted = isSpent
        };
        return true;
    }
}
