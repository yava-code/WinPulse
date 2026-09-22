namespace Pulse.Core.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Security;

public sealed class DeepSeekUsageService : IUsageProviderService
{
    public Provider Provider => Provider.DeepSeek;

    private readonly ICredentialStore _credentials;
    private readonly HttpClient _httpClient;
    private const string Endpoint = "https://api.deepseek.com/user/balance";

    public DeepSeekUsageService(ICredentialStore credentials, HttpClient? httpClient = null)
    {
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default)
    {
        var key = _credentials.GetKey(Provider.DeepSeek);
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderUsage.Unavailable(account, "Enter DeepSeek API key in Settings");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderUsage.Unavailable(account, $"DeepSeek API returned HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsageResponse(content, account);
        }
        catch (Exception ex)
        {
            return ProviderUsage.Unavailable(account, $"DeepSeek connection error: {ex.Message}");
        }
    }

    public static ProviderUsage ParseUsageResponse(string json, AccountKey account)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var isAvailable = root.TryGetProperty("is_available", out var avProp) && avProp.GetBoolean();
        string? balanceFormatted = null;

        if (root.TryGetProperty("balance_infos", out var infos) && infos.ValueKind == JsonValueKind.Array)
        {
            foreach (var info in infos.EnumerateArray())
            {
                var currency = info.TryGetProperty("currency", out var curr) ? curr.GetString() : "CNY";
                var total = info.TryGetProperty("total_balance", out var tb) ? tb.GetString() : null;
                if (!string.IsNullOrEmpty(total))
                {
                    balanceFormatted = $"{currency} {total}";
                    break;
                }
            }
        }

        var windows = new List<UsageWindow>();
        if (!string.IsNullOrEmpty(balanceFormatted))
        {
            windows.Add(new UsageWindow
            {
                Id = "deepseek.balance",
                Kind = WindowKind.Balance,
                Scope = "Balance",
                UsedFraction = isAvailable ? 0.0 : 1.0,
                ReportsLength = false,
                IsExhausted = !isAvailable
            });
        }

        return new ProviderUsage
        {
            Account = account,
            Windows = windows,
            ObservedAt = DateTime.UtcNow,
            State = windows.Count > 0 ? UsageState.Live : UsageState.Unavailable,
            CreditBalance = balanceFormatted,
            UnavailabilityReason = !isAvailable ? "Account balance depleted" : null
        };
    }
}
