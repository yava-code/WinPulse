namespace Pulse.Windows.SystemIntegration;

using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using Pulse.Core.Models;
using Pulse.Core.Security;

public sealed class GitHubCopilotOAuthService
{
    private readonly ICredentialStore _credentials;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public const string ClientId = "01ab8ac9400c4e429b23"; // Standard GitHub Copilot VS Code Client ID

    public GitHubCopilotOAuthService(ICredentialStore credentials)
    {
        _credentials = credentials;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> StartDeviceFlowAsync(Action<string> onUserCodeAvailable, CancellationToken cancellationToken = default)
    {
        // 1. Request device code
        var pairs = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "read:user"
        };

        using var reqContent = new FormUrlEncodedContent(pairs);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = reqContent
        };
        reqMsg.Headers.Accept.ParseAdd("application/json");

        using var initRes = await _http.SendAsync(reqMsg, cancellationToken);
        if (!initRes.IsSuccessStatusCode) return false;

        var initJson = await initRes.Content.ReadAsStringAsync(cancellationToken);
        using var initDoc = JsonDocument.Parse(initJson);
        var root = initDoc.RootElement;

        var deviceCode = root.GetProperty("device_code").GetString();
        var userCode = root.GetProperty("user_code").GetString();
        var verificationUri = root.GetProperty("verification_uri").GetString() ?? "https://github.com/login/device";
        var interval = root.TryGetProperty("interval", out var iProp) ? iProp.GetInt32() : 5;

        if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode)) return false;

        // Copy user code to clipboard on UI dispatcher thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try { Clipboard.SetText(userCode); } catch { }
        });

        onUserCodeAvailable(userCode);

        // Open browser
        Process.Start(new ProcessStartInfo
        {
            FileName = verificationUri,
            UseShellExecute = true
        });

        // Poll for authorization
        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, interval));
        var expiresAt = DateTime.UtcNow.AddMinutes(10);

        while (DateTime.UtcNow < expiresAt && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(pollInterval, cancellationToken);

            var pollPairs = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            };

            using var pollMsg = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(pollPairs)
            };
            pollMsg.Headers.Accept.ParseAdd("application/json");

            using var pollRes = await _http.SendAsync(pollMsg, cancellationToken);
            if (!pollRes.IsSuccessStatusCode) continue;

            var pollJson = await pollRes.Content.ReadAsStringAsync(cancellationToken);
            using var pollDoc = JsonDocument.Parse(pollJson);
            var pollRoot = pollDoc.RootElement;

            if (pollRoot.TryGetProperty("access_token", out var atProp))
            {
                var token = atProp.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    _credentials.SetKey(Provider.Copilot, token);
                    return true;
                }
            }

            if (pollRoot.TryGetProperty("error", out var errProp))
            {
                var err = errProp.GetString();
                if (err == "authorization_pending") continue;
                if (err == "slow_down")
                {
                    pollInterval += TimeSpan.FromSeconds(5);
                    continue;
                }
                // Terminal error (expired_token, access_denied)
                return false;
            }
        }

        return false;
    }
}
