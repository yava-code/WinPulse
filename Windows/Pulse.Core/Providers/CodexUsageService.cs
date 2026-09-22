namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;

public sealed class CodexUsageService : IUsageProviderService
{
    public Provider Provider => Provider.Codex;

    private readonly IPlatformPaths _paths;
    private readonly HttpClient _httpClient;
    private const string Endpoint = "https://chatgpt.com/backend-api/wham/usage";

    public CodexUsageService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    private sealed record Credentials(string AccessToken, string AccountId);

    private Credentials? LoadCredentials()
    {
        var authPath = _paths.GetCodexAuthPath();
        if (!File.Exists(authPath)) return null;

        try
        {
            var json = File.ReadAllText(authPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens)) return null;
            if (!tokens.TryGetProperty("access_token", out var tokenProp)) return null;

            var token = tokenProp.GetString();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var accountId = tokens.TryGetProperty("account_id", out var accProp) ? accProp.GetString() ?? "" : "";
            return new Credentials(token, accountId);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var creds = LoadCredentials();
        if (creds == null)
        {
            return ProviderUsage.Unavailable(account, "Sign in to Codex CLI first (missing or invalid auth.json)");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(creds.AccountId))
            {
                request.Headers.Add("ChatGPT-Account-Id", creds.AccountId);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsage.Unavailable(account, "Codex credentials expired. Run 'codex' in terminal to refresh.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"Codex server returned HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsageResponse(content, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageResponse(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var windows = new List<UsageWindow>();
        var spendReached = root.TryGetProperty("spend_control", out var sc) &&
                           sc.TryGetProperty("reached", out var scR) && scR.GetBoolean();

        if (root.TryGetProperty("rate_limit", out var rateLimit))
        {
            var isGroupSpent = IsGroupSpent(rateLimit) || spendReached;
            var parsed = ParseHttpWindows(rateLimit, "codex", scope: null);
            windows.AddRange(MarkSpent(parsed, isGroupSpent));
        }

        if (root.TryGetProperty("additional_rate_limits", out var extras) && extras.ValueKind == JsonValueKind.Array)
        {
            foreach (var extra in extras.EnumerateArray())
            {
                if (!extra.TryGetProperty("rate_limit", out var extraLimit)) continue;
                var scope = extra.TryGetProperty("limit_name", out var ln) ? ln.GetString() : null;
                var idPrefix = extra.TryGetProperty("metered_feature", out var mf) ? mf.GetString() ?? scope ?? "extra" : scope ?? "extra";

                var isGroupSpent = IsGroupSpent(extraLimit);
                var parsed = ParseHttpWindows(extraLimit, idPrefix, scope);
                windows.AddRange(MarkSpent(parsed, isGroupSpent));
            }
        }

        string? plan = null;
        if (root.TryGetProperty("plan_type", out var planProp))
        {
            plan = MapPlanName(planProp.GetString() ?? "");
        }

        string? creditBalance = null;
        if (root.TryGetProperty("credits", out var credits))
        {
            var unlimited = credits.TryGetProperty("unlimited", out var unl) && unl.GetBoolean();
            if (!unlimited && credits.TryGetProperty("balance", out var bal))
            {
                creditBalance = bal.GetString();
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            Plan = plan,
            CreditBalance = creditBalance,
            UnavailabilityReason = windows.Count == 0 ? "No limits reported by Codex" : null
        };
    }

    private static List<UsageWindow> ParseHttpWindows(JsonElement limitElement, string idPrefix, string? scope)
    {
        var list = new List<UsageWindow>();
        foreach (var slot in new[] { "primary_window", "secondary_window" })
        {
            if (!limitElement.TryGetProperty(slot, out var node)) continue;
            if (!node.TryGetProperty("used_percent", out var percentProp)) continue;

            var percent = percentProp.GetDouble();
            int? seconds = node.TryGetProperty("limit_window_seconds", out var secProp) ? secProp.GetInt32() : null;

            DateTime? resetsAt = null;
            if (node.TryGetProperty("reset_after_seconds", out var rasProp) && rasProp.TryGetInt32(out var ras) && ras > 0)
            {
                resetsAt = DateTime.UtcNow.AddSeconds(ras);
            }
            else if (node.TryGetProperty("reset_at", out var resetProp) && resetProp.ValueKind == JsonValueKind.Number)
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds((long)resetProp.GetDouble()).UtcDateTime;
            }

            list.Add(new UsageWindow
            {
                Id = $"{idPrefix}.{slot}",
                Kind = seconds.HasValue ? KindFromSeconds(seconds.Value) : WindowKind.Other,
                Scope = scope,
                UsedFraction = Math.Clamp(percent / 100.0, 0.0, 1.0),
                WindowSeconds = seconds ?? 0,
                ResetsAt = resetsAt
            });
        }
        return list;
    }

    private static bool IsGroupSpent(JsonElement limit)
    {
        return limit.TryGetProperty("limit_reached", out var lr) && lr.GetBoolean();
    }

    private static List<UsageWindow> MarkSpent(List<UsageWindow> windows, bool isSpent)
    {
        if (!isSpent || windows.Count == 0) return windows;
        var fullest = windows.MaxBy(w => w.UsedFraction);
        return windows.Select(w => w.Id == fullest?.Id ? w with { IsExhausted = true } : w).ToList();
    }

    private static WindowKind KindFromSeconds(int seconds) => seconds switch
    {
        18_000 => WindowKind.FiveHour,
        604_800 => WindowKind.Weekly,
        _ => WindowKind.Other
    };

    public static string MapPlanName(string raw) => raw.ToLowerInvariant() switch
    {
        "free" => "Free",
        "go" => "Go",
        "plus" => "Plus",
        "pro" => "Pro",
        "prolite" => "Pro 5x",
        "team" => "Team",
        "business" => "Business",
        "enterprise" => "Enterprise",
        "edu" => "Edu",
        _ => raw
    };
}
