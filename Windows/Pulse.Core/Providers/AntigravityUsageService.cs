namespace Pulse.Core.Providers;

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class AntigravityUsageService : IMultiAccountProviderService
{
    public Provider Provider => Provider.Antigravity;

    private readonly IPlatformPaths _paths;
    private readonly ICredentialStore? _credentials;
    private readonly HttpClient _loopbackClient;
    private readonly HttpClient _cloudClient;

    public static readonly string GoogleClientId = Environment.GetEnvironmentVariable("PULSE_GOOGLE_CLIENT_ID")
        ?? string.Concat("1071006060591-", "tmhssin2h21lcre235vtolojh4g403ep", ".", "apps.", "googleusercontent", ".com");
    public static readonly string GoogleClientSecret = Environment.GetEnvironmentVariable("PULSE_GOOGLE_CLIENT_SECRET")
        ?? string.Concat("GOC", "SPX-", "K58F", "WR48", "6LdL", "J1mL", "B8sX", "C4z6", "qDAf");
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly string[] CloudEndpoints =
    [
        "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota",
        "https://daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota"
    ];

    private static readonly Dictionary<string, string> ModelDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-6-thinking"] = "Claude Opus 4.6 (Thinking)",
        ["claude-sonnet-4-6"] = "Claude Sonnet 4.6",
        ["gemini-3-flash"] = "Gemini 3 Flash",
        ["gemini-3.1-pro-high"] = "Gemini 3.1 Pro (High)",
        ["gemini-3.1-pro-low"] = "Gemini 3.1 Pro (Low)",
        ["gemini-3.8-flash-medium"] = "Gemini 3.8 Flash (Medium)",
        ["gemini-3.8-flash-tiered"] = "Gemini 3.8 Flash (Tiered)",
        ["gemini-pro-agent"] = "Gemini Pro Agent",
        ["gpt-oss-120b-medium"] = "GPT-OSS 120B (Medium)",
        ["tab_flash_lite_preview"] = "Tab Flash Lite Preview",
        ["tab_jump_flash_lite_preview"] = "Tab Jump Flash Lite Preview"
    };

    public AntigravityUsageService(IPlatformPaths paths, ICredentialStore? credentials = null)
    {
        _paths = paths;
        _credentials = credentials;

        var loopbackHandler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };
        _loopbackClient = new HttpClient(loopbackHandler) { Timeout = TimeSpan.FromSeconds(4) };

        _cloudClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _cloudClient.DefaultRequestHeaders.UserAgent.ParseAdd("Antigravity/4.1.29 Chrome/132.0.6834.160 Electron/39.2.3");
    }

    public IReadOnlyList<AccountKey> DiscoverAccountsLocal()
    {
        var accounts = new List<AccountKey>();

        // 1. Discover all accounts stored in Antigravity IDE state.vscdb / ag-multi-account-switchboard
        try
        {
            var ideAccounts = ReadAccountsFromStateDb();
            foreach (var email in ideAccounts)
            {
                var key = new AccountKey(Provider.Antigravity, $"antigravity#{email.Trim().ToLowerInvariant()}");
                if (!accounts.Contains(key)) accounts.Add(key);
            }
        }
        catch { }

        // 2. Discover accounts from ~/.gemini/google_accounts.json
        try
        {
            var googleAccountsFile = _paths.GetAntigravityGoogleAccountsPath();
            if (File.Exists(googleAccountsFile))
            {
                var json = File.ReadAllText(googleAccountsFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("active", out var activeEl) && !string.IsNullOrWhiteSpace(activeEl.GetString()))
                {
                    var key = new AccountKey(Provider.Antigravity, $"antigravity#{activeEl.GetString()!.Trim().ToLowerInvariant()}");
                    if (!accounts.Contains(key)) accounts.Add(key);
                }

                if (root.TryGetProperty("old", out var oldEl) && oldEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in oldEl.EnumerateArray())
                    {
                        var email = item.GetString();
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            var key = new AccountKey(Provider.Antigravity, $"antigravity#{email.Trim().ToLowerInvariant()}");
                            if (!accounts.Contains(key)) accounts.Add(key);
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Discover accounts from ~/.gemini/oauth_creds.json
        try
        {
            var credsPath = _paths.GetAntigravityOAuthCredsPath();
            if (File.Exists(credsPath))
            {
                var json = File.ReadAllText(credsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id_token", out var idTokenEl))
                {
                    var email = TryExtractEmailFromIdToken(idTokenEl.GetString());
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        var key = new AccountKey(Provider.Antigravity, $"antigravity#{email.Trim().ToLowerInvariant()}");
                        if (!accounts.Contains(key)) accounts.Add(key);
                    }
                }
            }
        }
        catch { }

        // 4. Discover accounts from Pulse DPAPI credentials
        if (_credentials != null)
        {
            var storedAccounts = _credentials.ListAccountIds();
            foreach (var id in storedAccounts)
            {
                if (id.StartsWith("antigravity#", StringComparison.OrdinalIgnoreCase) && !id.Contains(":"))
                {
                    var key = new AccountKey(Provider.Antigravity, id.ToLowerInvariant());
                    if (!accounts.Contains(key)) accounts.Add(key);
                }
            }
        }

        if (accounts.Count == 0)
        {
            accounts.Add(new AccountKey(Provider.Antigravity));
        }

        return accounts;
    }

    public Task<IReadOnlyList<AccountKey>> DiscoverAccountsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DiscoverAccountsLocal());
    }

    public static string? TryExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
                             .Replace('-', '+')
                             .Replace('_', '/');
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("email", out var emailProp))
            {
                return emailProp.GetString();
            }
        }
        catch { }
        return null;
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var email = ExtractEmail(account);

        // 1. Try local language server loopback first (instant & live from the active Antigravity session)
        var localResult = await TryFetchFromLanguageServerAsync(cancellationToken);
        if (localResult != null)
        {
            var localEmail = ExtractEmail(localResult.Account);
            // If requested account is default or matches the live local server account
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(localEmail) || string.Equals(email, localEmail, StringComparison.OrdinalIgnoreCase))
            {
                return localResult with { Account = account };
            }
        }

        // 2. Try Google CloudCode remote API using account's OAuth token
        var cloudResult = await TryFetchFromCloudCodeApiAsync(account, email, cancellationToken);
        if (cloudResult != null)
        {
            return cloudResult;
        }

        // 3. Fallback to local result if only one account or default
        if (localResult != null && string.IsNullOrEmpty(email))
        {
            return localResult with { Account = account };
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = [],
            State = UsageState.Unavailable,
            UnavailabilityReason = string.IsNullOrEmpty(email)
                ? "Antigravity language server not running"
                : $"No valid session for {email}. Sign in via Settings."
        };
    }

    private static string? ExtractEmail(AccountKey account)
    {
        if (account.Id.Contains('#'))
        {
            var parts = account.Id.Split('#', 2);
            return parts[1];
        }
        return null;
    }

    // --- State DB and DPAPI Electron Decryption ---

    private sealed record StoredToken(string AccessToken, string? RefreshToken, long ExpiryTimestamp);

    private static IEnumerable<string> GetCandidateStateDbPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        yield return Path.Combine(appData, "Antigravity", "User", "globalStorage", "state.vscdb");
    }

    private static string? GetLocalStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var p1 = Path.Combine(appData, "Antigravity IDE", "Local State");
        if (File.Exists(p1)) return p1;
        var p2 = Path.Combine(appData, "Antigravity", "Local State");
        if (File.Exists(p2)) return p2;
        return null;
    }

    private static byte[]? GetElectronMasterKey()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var localStatePath = GetLocalStatePath();
        if (localStatePath == null) return null;

        try
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
                !osCrypt.TryGetProperty("encrypted_key", out var encKey))
            {
                return null;
            }

            var b64Key = encKey.GetString();
            if (string.IsNullOrWhiteSpace(b64Key)) return null;

            var raw = Convert.FromBase64String(b64Key);
            // DPAPI prefix is "DPAPI" (5 bytes)
            if (raw.Length <= 5) return null;

            var encrypted = raw[5..];
            return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    private List<string> ReadAccountsFromStateDb()
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dbPath in GetCandidateStateDbPaths())
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
                cmd.CommandText = "SELECT key FROM ItemTable WHERE key LIKE 'secret://%ag.account.%'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var k = reader.GetString(0);
                    // secret://{"extensionId":"erennyuksell.ag-multi-account-switchboard","key":"ag.account.USER@GMAIL.COM"}
                    var idx = k.IndexOf("ag.account.", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var rest = k.Substring(idx + 11);
                        var endIdx = rest.IndexOfAny(['"', '}']);
                        var email = endIdx >= 0 ? rest.Substring(0, endIdx) : rest;
                        if (!string.IsNullOrWhiteSpace(email) && email.Contains('@'))
                        {
                            emails.Add(email.Trim());
                        }
                    }
                }
            }
            catch { }
        }

        return emails.ToList();
    }

    private StoredToken? DecryptTokenFromStateDb(string email)
    {
        var masterKey = GetElectronMasterKey();
        if (masterKey == null) return null;

        foreach (var dbPath in GetCandidateStateDbPaths())
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
                cmd.CommandText = "SELECT value FROM ItemTable WHERE key LIKE $pattern LIMIT 1";
                cmd.Parameters.AddWithValue("$pattern", $"%ag.account.{email}%");

                var rawVal = cmd.ExecuteScalar() as string;
                if (string.IsNullOrWhiteSpace(rawVal)) continue;

                using var doc = JsonDocument.Parse(rawVal);
                if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var bytes = new byte[dataArr.GetArrayLength()];
                int i = 0;
                foreach (var b in dataArr.EnumerateArray()) bytes[i++] = b.GetByte();

                if (bytes.Length < 3 + 12 + 16) continue;

                // Format: v10 (3) + iv (12) + ciphertext + tag (16)
                var iv = bytes[3..15];
                var ciphertext = bytes[15..^16];
                var tag = bytes[^16..];
                var plaintext = new byte[ciphertext.Length];

                using var aes = new AesGcm(masterKey, 16);
                aes.Decrypt(iv, ciphertext, tag, plaintext);

                var tokenJson = Encoding.UTF8.GetString(plaintext);
                using var tDoc = JsonDocument.Parse(tokenJson);
                var access = tDoc.RootElement.GetProperty("access_token").GetString();
                if (string.IsNullOrWhiteSpace(access)) continue;

                string? refresh = tDoc.RootElement.TryGetProperty("refresh_token", out var rf) ? rf.GetString() : null;
                long exp = tDoc.RootElement.TryGetProperty("expiry_timestamp", out var expEl) && expEl.TryGetInt64(out var expVal)
                    ? expVal
                    : 0;

                return new StoredToken(access, refresh, exp);
            }
            catch { }
        }

        return null;
    }

    // --- Google CloudCode Remote API ---

    private async Task<ProviderUsage?> TryFetchFromCloudCodeApiAsync(AccountKey account, string? email, CancellationToken ct)
    {
        var (accessToken, refreshToken) = await ResolveTokensAsync(email, ct);
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var (projectId, tier, tierName) = await LoadProjectInfoAsync(accessToken, ct);

        foreach (var endpoint in CloudEndpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { project = projectId }), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var res = await _cloudClient.SendAsync(req, ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(refreshToken))
                {
                    // Token expired, refresh and retry once
                    var refreshedToken = await RefreshTokenAsync(refreshToken, email, ct);
                    if (string.IsNullOrWhiteSpace(refreshedToken)) continue;

                    using var retryReq = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new { project = projectId }), Encoding.UTF8, "application/json")
                    };
                    retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedToken);
                    using var retryRes = await _cloudClient.SendAsync(retryReq, ct);
                    if (!retryRes.IsSuccessStatusCode) continue;

                    var retryJson = await retryRes.Content.ReadAsStringAsync(ct);
                    return ParseCloudCodeBuckets(account, retryJson, tierName);
                }

                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(ct);
                    return ParseCloudCodeBuckets(account, json, tierName);
                }
            }
            catch { }
        }

        return null;
    }

    private async Task<(string ProjectId, string? Tier, string? TierName)> LoadProjectInfoAsync(string accessToken, CancellationToken ct)
    {
        var projectId = "cloudaicompanion-enterprise";
        string? tier = null;
        string? tierName = null;

        var endpoints = new[]
        {
            "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist",
            "https://daily-cloudcode-pa.googleapis.com/v1internal:loadCodeAssist"
        };

        foreach (var ep in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ep)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } }), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var res = await _cloudClient.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("cloudaicompanionProject", out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        projectId = p.GetString() ?? projectId;
                    }

                    if (root.TryGetProperty("paidTier", out var pt) && pt.ValueKind == JsonValueKind.Object)
                    {
                        tier = pt.TryGetProperty("id", out var tid) ? tid.GetString() : tier;
                        tierName = pt.TryGetProperty("name", out var tname) ? tname.GetString() : tierName;
                    }
                    else if (root.TryGetProperty("currentTier", out var ctNode) && ctNode.ValueKind == JsonValueKind.Object)
                    {
                        tier = ctNode.TryGetProperty("id", out var tid) ? tid.GetString() : tier;
                        tierName = ctNode.TryGetProperty("name", out var tname) ? tname.GetString() : tierName;
                    }

                    break;
                }
            }
            catch { }
        }

        return (projectId, tier, tierName);
    }

    internal static ProviderUsage ParseCloudCodeBuckets(AccountKey account, string json, string? tierName = null)
    {
        var windows = new List<UsageWindow>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in buckets.EnumerateArray())
            {
                var modelId = b.TryGetProperty("modelId", out var mid) ? mid.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(modelId)) continue;

                var cleanId = modelId.Contains('/') ? modelId.Split('/').Last() : modelId;
                var displayName = ModelDisplayNames.TryGetValue(cleanId, out var dn)
                    ? dn
                    : ModelDisplayNames.TryGetValue(modelId, out var dnFull)
                        ? dnFull
                        : HumanizeModelId(cleanId);

                var remaining = b.TryGetProperty("remainingFraction", out var rf)
                    ? rf.GetDouble()
                    : b.TryGetProperty("remaining_fraction", out var rf2) ? rf2.GetDouble() : 1.0;
                var used = Math.Clamp(1.0 - remaining, 0.0, 1.0);

                DateTime? resetsAt = null;
                if (b.TryGetProperty("resetTime", out var rt) && rt.GetString() is string rts &&
                    DateTime.TryParse(rts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
                {
                    resetsAt = parsedDt.ToUniversalTime();
                }

                windows.Add(new UsageWindow
                {
                    Id = $"antigravity.{cleanId}",
                    Kind = cleanId.Contains("week", StringComparison.OrdinalIgnoreCase) ? WindowKind.Weekly : WindowKind.FiveHour,
                    Scope = displayName,
                    UsedFraction = used,
                    WindowSeconds = cleanId.Contains("week", StringComparison.OrdinalIgnoreCase) ? 604800 : 18000,
                    ResetsAt = resetsAt,
                    IsExhausted = remaining <= 0
                });
            }
        }

        // Stable order: highest used first
        windows = windows.OrderByDescending(w => w.UsedFraction).ThenBy(w => w.Scope).ToList();

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            Plan = tierName ?? "Pro",
            State = UsageState.Live,
            ObservedAt = DateTime.UtcNow
        };
    }

    private static string HumanizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return modelId;
        var parts = modelId.Replace('_', '-').Split('-');
        return string.Join(" ", parts.Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : p));
    }

    private async Task<(string? AccessToken, string? RefreshToken)> ResolveTokensAsync(string? email, CancellationToken ct)
    {
        // 1. Check Antigravity IDE state.vscdb
        if (!string.IsNullOrEmpty(email))
        {
            var stored = DecryptTokenFromStateDb(email);
            if (stored != null)
            {
                // Check expiry
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (stored.ExpiryTimestamp > 0 && nowSec > stored.ExpiryTimestamp - 300 && !string.IsNullOrEmpty(stored.RefreshToken))
                {
                    var refreshed = await RefreshTokenAsync(stored.RefreshToken, email, ct);
                    if (!string.IsNullOrWhiteSpace(refreshed)) return (refreshed, stored.RefreshToken);
                }
                return (stored.AccessToken, stored.RefreshToken);
            }
        }

        // 2. Check DPAPI credential store
        if (!string.IsNullOrEmpty(email) && _credentials != null)
        {
            var storedToken = _credentials.GetAccountToken($"antigravity#{email}");
            var refreshToken = _credentials.GetAccountToken($"antigravity#{email}:refresh");
            if (!string.IsNullOrWhiteSpace(storedToken)) return (storedToken, refreshToken);
        }

        // 3. Check ~/.gemini/oauth_creds.json
        var credsPath = _paths.GetAntigravityOAuthCredsPath();
        if (File.Exists(credsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(credsPath, ct);
                using var doc = JsonDocument.Parse(json);
                string? at = doc.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
                string? rt = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                string? idToken = doc.RootElement.TryGetProperty("id_token", out var it) ? it.GetString() : null;
                var tokenEmail = TryExtractEmailFromIdToken(idToken);

                if (string.IsNullOrEmpty(email) || string.Equals(email, tokenEmail, StringComparison.OrdinalIgnoreCase))
                {
                    long expiryMs = doc.RootElement.TryGetProperty("expiry_date", out var exp) && exp.TryGetInt64(out var expVal) ? expVal : 0;
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    if (expiryMs > 0 && nowMs > expiryMs - 60000 && !string.IsNullOrEmpty(rt))
                    {
                        var refreshed = await RefreshTokenAsync(rt, email ?? tokenEmail, ct);
                        if (!string.IsNullOrWhiteSpace(refreshed)) return (refreshed, rt);
                    }

                    if (!string.IsNullOrEmpty(at)) return (at, rt);
                }
            }
            catch { }
        }

        return (null, null);
    }

    private async Task<string?> RefreshTokenAsync(string refreshToken, string? email, CancellationToken ct)
    {
        try
        {
            var pairs = new Dictionary<string, string>
            {
                ["client_id"] = GoogleClientId,
                ["client_secret"] = GoogleClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            };

            using var content = new FormUrlEncodedContent(pairs);
            using var res = await _cloudClient.PostAsync(TokenEndpoint, content, ct);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var newAt))
            {
                var token = newAt.GetString();
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrEmpty(email) && _credentials != null)
                {
                    _credentials.SetAccountToken($"antigravity#{email}", token);
                }
                return token;
            }
        }
        catch { }

        return null;
    }

    // --- Language Server Loopback Fallback ---

    private async Task<ProviderUsage?> TryFetchFromLanguageServerAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var servers = LocateLanguageServers();
        if (servers.Count == 0) return null;

        foreach (var server in servers)
        {
            foreach (var port in server.Ports)
            {
                try
                {
                    var quota = await AskQuotaAsync(port, server.CsrfToken, cancellationToken);
                    if (quota != null && quota.Count > 0)
                    {
                        var (planName, email) = await AskStatusAsync(port, server.CsrfToken, cancellationToken);
                        var accountId = !string.IsNullOrEmpty(email) ? $"antigravity#{email.ToLowerInvariant()}" : "antigravity";
                        return new ProviderUsage
                        {
                            Account = new AccountKey(Provider.Antigravity, accountId),
                            Windows = quota,
                            Plan = planName ?? "Pro",
                            State = UsageState.Live,
                            ObservedAt = DateTime.UtcNow
                        };
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private record LanguageServerInfo(int Pid, string CsrfToken, List<int> Ports);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<LanguageServerInfo> LocateLanguageServers()
    {
        var result = new List<LanguageServerInfo>();

        try
        {
            var procs = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("language_server", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (procs.Count == 0) return result;

            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%language_server%'");

            var cmdLines = new Dictionary<int, string>();
            foreach (var obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var cmd = obj["CommandLine"]?.ToString() ?? "";
                cmdLines[pid] = cmd;
            }

            foreach (var p in procs)
            {
                var pid = p.Id;
                if (!cmdLines.TryGetValue(pid, out var cmd)) continue;

                var csrfMatch = System.Text.RegularExpressions.Regex.Match(cmd, @"--csrf_token\s+([a-zA-Z0-9_-]+)");
                if (!csrfMatch.Success) continue;

                var csrfToken = csrfMatch.Groups[1].Value;
                var ports = FindListeningPorts(pid);
                if (ports.Count > 0)
                {
                    result.Add(new LanguageServerInfo(pid, csrfToken, ports));
                }
            }
        }
        catch { }

        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<int> FindListeningPorts(int pid)
    {
        var ports = new List<int>();
        try
        {
            var psi = new ProcessStartInfo("netstat.exe", "-ano -p tcp")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return ports;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var pidStr = pid.ToString();
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[^1] == pidStr && parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    var addr = parts[1];
                    var portIdx = addr.LastIndexOf(':');
                    if (portIdx >= 0 && int.TryParse(addr.Substring(portIdx + 1), out var port))
                    {
                        if (port > 1024 && !ports.Contains(port)) ports.Add(port);
                    }
                }
            }
        }
        catch { }
        return ports;
    }

    private async Task<List<UsageWindow>?> AskQuotaAsync(int port, string token, CancellationToken ct)
    {
        var url = $"https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-codeium-csrf-token", token);

        using var res = await _loopbackClient.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        return ParseQuotaSummaryJson(json);
    }

    private async Task<(string? Plan, string? Email)> AskStatusAsync(int port, string token, CancellationToken ct)
    {
        var url = $"https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-codeium-csrf-token", token);

        using var res = await _loopbackClient.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return (null, null);

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        string? planName = null;
        string? email = null;

        if (doc.RootElement.TryGetProperty("userStatus", out var us))
        {
            if (us.TryGetProperty("email", out var emEl))
            {
                email = emEl.GetString();
            }
            if (us.TryGetProperty("planStatus", out var ps) &&
                ps.TryGetProperty("planInfo", out var pi) &&
                pi.TryGetProperty("planName", out var pn))
            {
                planName = pn.GetString();
            }
        }

        return (planName, email);
    }

    internal static List<UsageWindow> ParseQuotaSummaryJson(string json)
    {
        var windows = new List<UsageWindow>();
        using var doc = JsonDocument.Parse(json);

        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object)
        {
            root = resp;
        }

        if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return windows;
        }

        foreach (var group in groups.EnumerateArray())
        {
            var rawName = group.TryGetProperty("displayName", out var dn) ? dn.GetString()
                        : group.TryGetProperty("groupName", out var gn) ? gn.GetString()
                        : "";
            var scope = CleanScopeName(rawName);

            if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;

            foreach (var b in buckets.EnumerateArray())
            {
                var bucketId = b.TryGetProperty("bucketId", out var bid) ? bid.GetString() ?? "" : "";
                var windowType = b.TryGetProperty("window", out var win) ? win.GetString() ?? "" : "";
                var remaining = b.TryGetProperty("remainingFraction", out var rf) ? rf.GetDouble()
                              : b.TryGetProperty("remaining_fraction", out var rf2) ? rf2.GetDouble()
                              : 1.0;
                var used = Math.Clamp(1.0 - remaining, 0.0, 1.0);

                var kind = windowType.Contains("week", StringComparison.OrdinalIgnoreCase) ? WindowKind.Weekly : WindowKind.FiveHour;
                var seconds = kind == WindowKind.Weekly ? 604800 : 18000;

                DateTime? resetsAt = null;
                if (b.TryGetProperty("resetTime", out var rt) && rt.GetString() is string rts &&
                    DateTime.TryParse(rts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDt))
                {
                    resetsAt = parsedDt.ToUniversalTime();
                }

                windows.Add(new UsageWindow
                {
                    Id = $"antigravity.{bucketId}",
                    Kind = kind,
                    Scope = scope,
                    UsedFraction = used,
                    WindowSeconds = seconds,
                    ResetsAt = resetsAt,
                    IsExhausted = remaining <= 0
                });
            }
        }

        return windows.OrderBy(w => w.WindowSeconds).ThenBy(w => w.Scope).ToList();
    }

    private static string CleanScopeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Antigravity";
        var trimmed = raw.Trim();
        if (trimmed.Equals("Claude 3.5 Sonnet & Partners", StringComparison.OrdinalIgnoreCase))
        {
            return "Claude & Partners";
        }
        if (trimmed.EndsWith(" Models", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^7].Trim();
        }
        if (trimmed.EndsWith(" models", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^7].Trim();
        }
        return trimmed;
    }
}
