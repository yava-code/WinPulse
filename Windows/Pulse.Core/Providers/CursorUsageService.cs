namespace Pulse.Core.Providers;

using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core.Models;
using Pulse.Core.Platform;

public sealed class CursorUsageService : IUsageProviderService
{
    public Provider Provider => Provider.Cursor;

    private readonly IPlatformPaths _paths;
    private readonly HttpClient _httpClient;
    private const string SummaryEndpoint = "https://cursor.com/api/usage-summary";
    private const string LegacyEndpoint = "https://cursor.com/api/usage";

    public CursorUsageService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    private string? GetSessionCookie()
    {
        var dbPath = _paths.GetCursorStateDbPath();
        if (!File.Exists(dbPath)) return null;

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var connection = new SqliteConnection(connStr);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = 'cursorAuth/accessToken' LIMIT 1;";
            var result = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(result)) return null;

            return BuildSessionCookie(result);
        }
        catch
        {
            return null;
        }
    }

    public static string? BuildSessionCookie(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sub", out var subProp)) return null;
            var sub = subProp.GetString();
            if (string.IsNullOrEmpty(sub)) return null;

            if (root.TryGetProperty("exp", out var expProp))
            {
                var expSeconds = expProp.GetDouble();
                var expDate = DateTimeOffset.FromUnixTimeSeconds((long)expSeconds).UtcDateTime;
                if (DateTime.UtcNow.AddSeconds(60) > expDate)
                {
                    return null; // Expired
                }
            }

            var accountId = sub.Contains('|') ? sub.Split('|').Last() : sub;
            return $"WorkosCursorSessionToken={accountId}%3A%3A{token}";
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        var bytes = Convert.FromBase64String(output);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var cookie = GetSessionCookie();
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return ProviderUsage.Unavailable(account, "Sign in to Cursor editor first (state.vscdb session not found)");
        }

        try
        {
            // 1. Try modern usage-summary endpoint first
            using var request = new HttpRequestMessage(HttpMethod.Get, SummaryEndpoint);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", "Pulse-Windows/1.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Cursor session expired. Open Cursor to refresh token.");
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var usage = ParseUsageResponse(content, account);
                if (usage.Windows.Count > 0) return usage;
            }

            // 2. Fallback to legacy endpoint if summary produced no windows
            using var legacyReq = new HttpRequestMessage(HttpMethod.Get, LegacyEndpoint);
            legacyReq.Headers.Add("Cookie", cookie);
            legacyReq.Headers.Add("Accept", "application/json");
            legacyReq.Headers.Add("User-Agent", "Pulse-Windows/1.0");

            using var legacyResp = await _httpClient.SendAsync(legacyReq, cancellationToken);
            if (legacyResp.IsSuccessStatusCode)
            {
                var legacyContent = await legacyResp.Content.ReadAsStringAsync(cancellationToken);
                return ParseUsageResponse(legacyContent, account);
            }

            return ProviderUsage.Unavailable(account, $"Cursor API returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Cursor connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageResponse(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();
        string? plan = null;

        if (root.TryGetProperty("membershipType", out var memProp) && memProp.ValueKind == JsonValueKind.String)
        {
            var raw = memProp.GetString() ?? "";
            plan = char.ToUpperInvariant(raw[0]) + raw.Substring(1).ToLowerInvariant();
        }

        DateTime? billingCycleEnd = null;
        if (root.TryGetProperty("billingCycleEnd", out var bceProp) && bceProp.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(bceProp.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
            {
                billingCycleEnd = parsedDt.ToUniversalTime();
            }
        }

        // Shape A: usage-summary (individualUsage.plan or teamUsage.pooled)
        JsonElement? planNode = null;
        if (root.TryGetProperty("individualUsage", out var iu) && iu.ValueKind == JsonValueKind.Object &&
            iu.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            planNode = p;
        }
        else if (root.TryGetProperty("teamUsage", out var tu) && tu.ValueKind == JsonValueKind.Object &&
                 tu.TryGetProperty("pooled", out var pooled) && pooled.ValueKind == JsonValueKind.Object)
        {
            planNode = pooled;
        }

        if (planNode.HasValue)
        {
            var node = planNode.Value;
            if (node.TryGetProperty("autoPercentUsed", out var apu) && apu.ValueKind == JsonValueKind.Number)
            {
                var pct = apu.GetDouble();
                windows.Add(new UsageWindow
                {
                    Id = "cursor.models",
                    Kind = WindowKind.Monthly,
                    Scope = "Cursor Models",
                    UsedFraction = Math.Clamp(pct / 100.0, 0.0, 1.0),
                    WindowSeconds = 30 * 86400,
                    ResetsAt = billingCycleEnd,
                    ReportsLength = false,
                    IsExhausted = pct >= 100
                });
            }

            if (node.TryGetProperty("apiPercentUsed", out var apiPct) && apiPct.ValueKind == JsonValueKind.Number)
            {
                var pct = apiPct.GetDouble();
                windows.Add(new UsageWindow
                {
                    Id = "cursor.other",
                    Kind = WindowKind.Monthly,
                    Scope = "Other Models",
                    UsedFraction = Math.Clamp(pct / 100.0, 0.0, 1.0),
                    WindowSeconds = 30 * 86400,
                    ResetsAt = billingCycleEnd,
                    ReportsLength = false,
                    IsExhausted = pct >= 100
                });
            }
        }

        // On-demand spend pool
        JsonElement? onDemandNode = null;
        if (root.TryGetProperty("individualUsage", out var iu2) && iu2.ValueKind == JsonValueKind.Object &&
            iu2.TryGetProperty("onDemand", out var od) && od.ValueKind == JsonValueKind.Object)
        {
            onDemandNode = od;
        }
        else if (root.TryGetProperty("teamUsage", out var tu2) && tu2.ValueKind == JsonValueKind.Object &&
                 tu2.TryGetProperty("onDemand", out var tod) && tod.ValueKind == JsonValueKind.Object)
        {
            onDemandNode = tod;
        }

        if (onDemandNode.HasValue)
        {
            var odVal = onDemandNode.Value;
            var enabled = odVal.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
            if (enabled && odVal.TryGetProperty("used", out var u) && u.ValueKind == JsonValueKind.Number &&
                odVal.TryGetProperty("limit", out var lim) && lim.ValueKind == JsonValueKind.Number)
            {
                var usedCents = u.GetDouble();
                var limitCents = lim.GetDouble();
                if (limitCents > 0)
                {
                    windows.Add(new UsageWindow
                    {
                        Id = "cursor.ondemand",
                        Kind = WindowKind.Spend,
                        Scope = "On-Demand Spend",
                        UsedFraction = Math.Clamp(usedCents / limitCents, 0.0, 1.0),
                        WindowSeconds = 30 * 86400,
                        ResetsAt = billingCycleEnd,
                        ReportsLength = false,
                        IsExhausted = usedCents >= limitCents
                    });
                }
            }
        }

        // Shape B: legacy /api/usage (e.g. "gpt-4", "claude-3-5-sonnet") - with null checks
        if (windows.Count == 0 && root.TryGetProperty("gpt-4", out var gpt4) && gpt4.ValueKind == JsonValueKind.Object)
        {
            var used = gpt4.TryGetProperty("numRequests", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
            var max = gpt4.TryGetProperty("maxRequestUsage", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0;
            if (max > 0)
            {
                windows.Add(new UsageWindow
                {
                    Id = "cursor.fast",
                    Kind = WindowKind.Monthly,
                    Scope = "Fast Requests",
                    UsedFraction = (double)used / max,
                    WindowSeconds = 30 * 86400,
                    ResetsAt = billingCycleEnd,
                    ReportsLength = false,
                    IsExhausted = used >= max
                });
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            Plan = plan,
            UnavailabilityReason = windows.Count == 0 ? "No active Cursor request limits" : null
        };
    }
}
