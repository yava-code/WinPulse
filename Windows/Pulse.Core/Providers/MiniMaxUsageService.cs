namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class MiniMaxUsageService : IUsageProviderService
{
    public Provider Provider { get; }

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;

    public MiniMaxUsageService(Provider provider, IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        Provider = provider;
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string ApiHost => Provider == Provider.MiniMaxCN ? "https://api.minimaxi.com" : "https://api.minimax.io";

    private string? GetApiKey()
    {
        return _credentials?.GetKey(Provider);
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderUsage.Unavailable(account, "MiniMax API key required (enter in Settings)");
        }

        var endpoints = new[]
        {
            $"{ApiHost}/v1/token_plan/remains",
            $"{ApiHost}/v1/api/openplatform/coding_plan/remains"
        };

        foreach (var ep in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ep);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(cancellationToken);
                    return ParsePlanRemains(json, account);
                }
            }
            catch { }
        }

        return ProviderUsage.Unavailable(account, "Unable to reach MiniMax coding plan API");
    }

    public static ProviderUsage ParsePlanRemains(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();

        // MiniMax reports remaining percent: remaining 96% means 4% used
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("current_plan_remaining_percent", out var remProp))
            {
                double rem = 100;
                if (remProp.ValueKind == JsonValueKind.Number) rem = remProp.GetDouble();
                else if (remProp.ValueKind == JsonValueKind.String) double.TryParse(remProp.GetString(), out rem);

                var used = Math.Clamp((100.0 - rem) / 100.0, 0.0, 1.0);
                windows.Add(new UsageWindow
                {
                    Id = "minimax.coding",
                    Kind = WindowKind.Monthly,
                    Scope = "Coding Plan",
                    UsedFraction = used,
                    WindowSeconds = 30 * 86400,
                    IsExhausted = rem <= 0
                });
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            UnavailabilityReason = windows.Count == 0 ? "No MiniMax limits reported" : null
        };
    }
}
