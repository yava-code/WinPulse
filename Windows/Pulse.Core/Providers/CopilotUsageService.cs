namespace Pulse.Core.Providers;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class CopilotUsageService : IUsageProviderService
{
    public Provider Provider => Provider.Copilot;

    private readonly ICredentialStore _credentials;
    private readonly IPlatformPaths? _paths;
    private readonly HttpClient _http;

    private static readonly Uri Endpoint = new("https://api.github.com/copilot_internal/user");

    public CopilotUsageService(ICredentialStore credentials, IPlatformPaths? paths = null)
    {
        _credentials = credentials;
        _paths = paths;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse-Windows/1.0");
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var token = _credentials.GetKey(Provider.Copilot);

        // Fallback: Check hosts.json or environment
        if (string.IsNullOrWhiteSpace(token))
        {
            token = TryFindCopilotToken();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new ProviderUsage
            {
                Account = account,
                Windows = [],
                State = UsageState.Unavailable,
                UnavailabilityReason = "Sign in to GitHub Copilot via Settings"
            };
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            req.Headers.Add("Authorization", $"token {token.Trim()}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("X-Github-Api-Version", "2025-04-01");
            req.Headers.Add("Editor-Version", "vscode/1.96.2");
            req.Headers.Add("Editor-Plugin-Version", "copilot-chat/0.26.7");

            using var res = await _http.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return ProviderUsage.Unavailable(account, "GitHub Copilot token expired or invalid");
                }
                return ProviderUsage.Unavailable(account, $"Copilot HTTP {(int)res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return ParseCopilotJson(account, json);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"Error: {ex.Message}");
        }
    }

    internal static ProviderUsage ParseCopilotJson(AccountKey account, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var plan = "Copilot";
        if (root.TryGetProperty("copilot_plan", out var planProp) && planProp.GetString() is string p && !string.IsNullOrWhiteSpace(p))
        {
            plan = p switch
            {
                "individual" => "Individual",
                "business" => "Business",
                "enterprise" => "Enterprise",
                "free" => "Free",
                _ => p
            };
        }

        DateTime? resetsAt = null;
        if (root.TryGetProperty("quota_reset_date_utc", out var rUtc) && rUtc.GetString() is string rUtcStr &&
            DateTime.TryParse(rUtcStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedUtc))
        {
            resetsAt = parsedUtc.ToUniversalTime();
        }
        else if (root.TryGetProperty("quota_reset_date", out var rDate) && rDate.GetString() is string rDateStr &&
                 DateTime.TryParse(rDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            resetsAt = parsedDate.ToUniversalTime();
        }

        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("quota_snapshots", out var snapshots) && snapshots.ValueKind == JsonValueKind.Object)
        {
            var lanes = new (string key, string scope)[]
            {
                ("premium_interactions", "Premium requests"),
                ("chat", "Chat"),
                ("completions", "Completions")
            };

            foreach (var (key, scope) in lanes)
            {
                if (!snapshots.TryGetProperty(key, out var snap) || snap.ValueKind != JsonValueKind.Object) continue;

                var unlimited = snap.TryGetProperty("unlimited", out var unlimEl) && unlimEl.GetBoolean();
                if (unlimited) continue;

                if (snap.TryGetProperty("has_quota", out var hasQuotaEl) && !hasQuotaEl.GetBoolean()) continue;

                double remaining = 100.0;
                if (snap.TryGetProperty("percent_remaining", out var prEl))
                {
                    remaining = prEl.GetDouble();
                }

                var used = Math.Clamp((100.0 - remaining) / 100.0, 0.0, 1.0);

                var overagePermitted = snap.TryGetProperty("overage_permitted", out var opEl) && opEl.GetBoolean();
                var isExhausted = remaining <= 0 && !overagePermitted;

                windows.Add(new UsageWindow
                {
                    Id = $"copilot.{key}",
                    Kind = WindowKind.Monthly,
                    Scope = scope,
                    UsedFraction = used,
                    WindowSeconds = 30 * 86400,
                    ResetsAt = resetsAt,
                    IsExhausted = isExhausted
                });
            }
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            Plan = plan,
            State = UsageState.Live,
            ObservedAt = DateTime.UtcNow
        };
    }

    private static string? TryFindCopilotToken()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var hostsFile = Path.Combine(appData, "github-copilot", "hosts.json");
            if (File.Exists(hostsFile))
            {
                var json = File.ReadAllText(hostsFile);
                using var doc = JsonDocument.Parse(json);
                foreach (var host in doc.RootElement.EnumerateObject())
                {
                    if (host.Value.TryGetProperty("oauth_token", out var tok))
                    {
                        var str = tok.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) return str;
                    }
                }
            }
        }
        catch { }

        return null;
    }
}
