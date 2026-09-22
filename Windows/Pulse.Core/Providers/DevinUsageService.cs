namespace Pulse.Core.Providers;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class DevinUsageService : IUsageProviderService
{
    public Provider Provider => Provider.Devin;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _httpClient;

    public DevinUsageService(IPlatformPaths paths, ICredentialStore? credentials = null, HttpClient? httpClient = null)
    {
        _paths = paths;
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        // Check Windsurf / Devin state.vscdb
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidateDbs = new[]
        {
            Path.Combine(appData, "Devin", "User", "globalStorage", "state.vscdb"),
            Path.Combine(appData, "Windsurf", "User", "globalStorage", "state.vscdb")
        };

        foreach (var dbPath in candidateDbs)
        {
            if (!File.Exists(dbPath)) continue;
            try
            {
                var connStr = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                using var conn = new SqliteConnection(connStr);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT value FROM ItemTable WHERE key LIKE 'windsurf.reactSettings.cachedPlanInfoData%' LIMIT 1";
                var raw = cmd.ExecuteScalar() as string;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var usage = ParsePlanInfoJson(raw, account);
                    if (usage.Windows.Count > 0) return Task.FromResult(usage);
                }
            }
            catch { }
        }

        return Task.FromResult(ProviderUsage.Unavailable(account, "Sign in to Devin / Windsurf editor first"));
    }

    public static ProviderUsage ParsePlanInfoJson(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var windows = new List<UsageWindow>();
        string? plan = null;

        if (root.TryGetProperty("planName", out var pn) && pn.ValueKind == JsonValueKind.String)
        {
            plan = pn.GetString();
        }

        if (root.TryGetProperty("promptCredits", out var pc) && pc.ValueKind == JsonValueKind.Object)
        {
            var used = pc.TryGetProperty("used", out var u) && u.TryGetDouble(out var uVal) ? uVal : 0;
            var total = pc.TryGetProperty("total", out var t) && t.TryGetDouble(out var tVal) ? tVal : 0;
            if (total > 0)
            {
                windows.Add(new UsageWindow
                {
                    Id = "devin.prompts",
                    Kind = WindowKind.Monthly,
                    Scope = "Prompts",
                    UsedFraction = Math.Clamp(used / total, 0.0, 1.0),
                    WindowSeconds = 30 * 86400,
                    IsExhausted = used >= total
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
            UnavailabilityReason = windows.Count == 0 ? "No active Devin limits found" : null
        };
    }
}
