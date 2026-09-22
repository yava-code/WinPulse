namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class CommandCodeUsageService : IUsageProviderService
{
    public Provider Provider => Provider.CommandCode;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.commandcode.ai";

    public CommandCodeUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string? GetApiKey()
    {
        var stored = _credentials?.GetKey(Provider.CommandCode);
        if (!string.IsNullOrWhiteSpace(stored)) return stored;

        var authPath = _paths.GetCommandCodeAuthPath();
        if (!File.Exists(authPath)) return null;

        try
        {
            var json = File.ReadAllText(authPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("apiKey", out var k) && !string.IsNullOrWhiteSpace(k.GetString()))
            {
                return k.GetString();
            }
        }
        catch { }

        return null;
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderUsage.Unavailable(account, "Command Code API key required (~/.commandcode/auth.json)");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/alpha/billing/credits");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Command Code API key refused.");
            }

            if (!res.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"Command Code server returned HTTP {(int)res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return ParseCreditsJson(json, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Command Code connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseCreditsJson(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();
        string? creditBalance = null;

        if (root.TryGetProperty("remainingCredits", out var rc) && rc.TryGetDouble(out var bal))
        {
            creditBalance = $"${bal:F2}";
        }

        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var lim in limits.EnumerateArray())
            {
                var name = lim.TryGetProperty("name", out var n) ? n.GetString() ?? "Limit" : "Limit";
                var pct = lim.TryGetProperty("percentUsed", out var pu) && pu.TryGetDouble(out var pVal) ? pVal : 0.0;
                var seconds = lim.TryGetProperty("windowSeconds", out var ws) && ws.TryGetInt32(out var sVal) ? sVal : 86400;

                windows.Add(new UsageWindow
                {
                    Id = $"commandcode.{name.ToLowerInvariant()}",
                    Kind = seconds <= 18000 ? WindowKind.FiveHour : WindowKind.Daily,
                    Scope = name,
                    UsedFraction = Math.Clamp(pct / 100.0, 0.0, 1.0),
                    WindowSeconds = seconds,
                    IsExhausted = pct >= 100
                });
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = UsageState.Live,
            CreditBalance = creditBalance,
            UnavailabilityReason = null
        };
    }
}
