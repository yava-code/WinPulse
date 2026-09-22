namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class KimiCodeUsageService : IUsageProviderService
{
    public Provider Provider => Provider.KimiCode;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;
    private const string Endpoint = "https://api.kimi.com/coding/v1/usages";

    public KimiCodeUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string? GetApiKey()
    {
        return _credentials?.GetKey(Provider.KimiCode);
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderUsage.Unavailable(account, "Kimi Code API key required (enter in Settings)");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Kimi Code API key refused.");
            }

            if (!res.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"Kimi server returned HTTP {(int)res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsageJson(json, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Kimi connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageJson(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();
        string? plan = null;

        if (root.TryGetProperty("user", out var u) && u.TryGetProperty("membership", out var m) &&
            m.TryGetProperty("level", out var lvl) && lvl.ValueKind == JsonValueKind.String)
        {
            plan = lvl.GetString();
        }

        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var lim in limits.EnumerateArray())
            {
                int durationSeconds = 18000;
                if (lim.TryGetProperty("window", out var win) && win.TryGetProperty("duration", out var dur) && dur.TryGetInt32(out var dVal))
                {
                    var unit = win.TryGetProperty("timeUnit", out var tu) ? tu.GetString() ?? "m" : "m";
                    durationSeconds = unit.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? dVal * 3600
                        : unit.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? dVal * 86400
                        : dVal * 60;
                }

                if (lim.TryGetProperty("detail", out var det))
                {
                    var w = ParseDetail(det, $"kimi.limit.{idx++}.{durationSeconds}", durationSeconds, reportsLength: true);
                    if (w != null) windows.Add(w);
                }
            }
        }

        if (root.TryGetProperty("usage", out var weeklyUsage))
        {
            var w = ParseDetail(weeklyUsage, "kimi.weekly", 7 * 86400, reportsLength: false);
            if (w != null) windows.Add(w);
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows.OrderBy(w => w.WindowSeconds).ToList(),
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            Plan = plan,
            UnavailabilityReason = windows.Count == 0 ? "No limits reported by Kimi Code" : null
        };
    }

    private static UsageWindow? ParseDetail(JsonElement detail, string id, int seconds, bool reportsLength)
    {
        double limit = 0;
        double used = 0;

        if (detail.TryGetProperty("limit", out var lProp) && lProp.ValueKind == JsonValueKind.String)
        {
            double.TryParse(lProp.GetString(), out limit);
        }
        else if (lProp.ValueKind == JsonValueKind.Number)
        {
            limit = lProp.GetDouble();
        }

        if (limit <= 0) return null;

        if (detail.TryGetProperty("used", out var uProp) && uProp.ValueKind == JsonValueKind.String)
        {
            double.TryParse(uProp.GetString(), out used);
        }
        else if (uProp.ValueKind == JsonValueKind.Number)
        {
            used = uProp.GetDouble();
        }
        else if (detail.TryGetProperty("remaining", out var rProp))
        {
            double rem = 0;
            if (rProp.ValueKind == JsonValueKind.String) double.TryParse(rProp.GetString(), out rem);
            else if (rProp.ValueKind == JsonValueKind.Number) rem = rProp.GetDouble();
            used = Math.Max(0, limit - rem);
        }

        DateTime? resetsAt = null;
        if (detail.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(rt.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
        {
            resetsAt = parsedDt.ToUniversalTime();
        }

        return new UsageWindow
        {
            Id = id,
            Kind = seconds <= 18000 ? WindowKind.FiveHour : WindowKind.Weekly,
            Scope = null,
            UsedFraction = Math.Clamp(used / limit, 0.0, 1.0),
            WindowSeconds = seconds,
            ResetsAt = resetsAt,
            ReportsLength = reportsLength,
            IsExhausted = used >= limit
        };
    }
}
