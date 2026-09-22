namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class OpenCodeGoUsageService : IUsageProviderService
{
    public Provider Provider => Provider.OpenCodeGo;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;
    private const string Endpoint = "https://opencode.ai/zen/go/v1/usage";

    public OpenCodeGoUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string? GetApiKey()
    {
        // 1. Check credential store
        var stored = _credentials?.GetKey(Provider.OpenCodeGo);
        if (!string.IsNullOrWhiteSpace(stored)) return stored;

        // 2. Candidate files on Windows
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ai.opencode.desktop", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "auth.json")
        };

        foreach (var p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var json = File.ReadAllText(p);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("opencode-go", out var entry) &&
                    entry.TryGetProperty("key", out var k) && !string.IsNullOrWhiteSpace(k.GetString()))
                {
                    return k.GetString();
                }
                if (doc.RootElement.TryGetProperty("key", out var directKey) && !string.IsNullOrWhiteSpace(directKey.GetString()))
                {
                    return directKey.GetString();
                }
            }
            catch { }
        }

        return null;
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderUsage.Unavailable(account, "OpenCode API key required (enter in Settings)");
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
                return ProviderUsage.Unavailable(account, "OpenCode API key refused.");
            }

            if (!res.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"OpenCode server returned HTTP {(int)res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsageJson(json, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"OpenCode connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageJson(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            AddWindow(windows, usage, "rolling", WindowKind.FiveHour, 5 * 3600);
            AddWindow(windows, usage, "weekly", WindowKind.Weekly, 7 * 86400);
            AddWindow(windows, usage, "monthly", WindowKind.Monthly, 30 * 86400);
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            UnavailabilityReason = windows.Count == 0 ? "No limits reported by OpenCode" : null
        };
    }

    private static void AddWindow(List<UsageWindow> windows, JsonElement usage, string key, WindowKind kind, int seconds)
    {
        if (!usage.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object) return;
        if (!node.TryGetProperty("percent", out var p) || p.ValueKind != JsonValueKind.Number) return;

        var pct = p.GetDouble();
        var status = node.TryGetProperty("status", out var s) ? s.GetString() : "ok";
        var isExhausted = !string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);

        DateTime? resetsAt = null;
        if (node.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(r.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
        {
            resetsAt = parsedDt.ToUniversalTime();
        }

        windows.Add(new UsageWindow
        {
            Id = $"opencode.{key}",
            Kind = kind,
            Scope = null,
            UsedFraction = Math.Clamp(pct / 100.0, 0.0, 1.0),
            WindowSeconds = seconds,
            ResetsAt = resetsAt,
            IsExhausted = isExhausted
        });
    }
}
