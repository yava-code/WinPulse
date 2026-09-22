namespace Pulse.Windows.SystemIntegration;

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulse.Core.Security;

public sealed class GoogleOAuthService
{
    private readonly ICredentialStore _credentials;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static readonly string ClientId = Pulse.Core.Providers.AntigravityUsageService.GoogleClientId;
    public static readonly string ClientSecret = Pulse.Core.Providers.AntigravityUsageService.GoogleClientSecret;

    public GoogleOAuthService(ICredentialStore credentials)
    {
        _credentials = credentials;
    }

    public async Task<string?> StartOAuthFlowAsync(CancellationToken cancellationToken = default)
    {
        // Use a dynamic loopback port matching Google's registered desktop client configuration
        var port = 19876 + Random.Shared.Next(100);
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        var prefix = $"http://127.0.0.1:{port}/callback/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch
        {
            // If random port collided, try fallback port
            port = 19999;
            redirectUri = $"http://127.0.0.1:{port}/callback";
            prefix = $"http://127.0.0.1:{port}/callback/";
            listener.Prefixes.Clear();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch
            {
                return null;
            }
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var scopes = "https://www.googleapis.com/auth/cloud-platform " +
                     "https://www.googleapis.com/auth/userinfo.email " +
                     "https://www.googleapis.com/auth/userinfo.profile " +
                     "https://www.googleapis.com/auth/cclog " +
                     "https://www.googleapis.com/auth/experimentsandconfigs";

        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&access_type=offline" +
            $"&prompt=consent" +
            $"&state={Uri.EscapeDataString(state)}";

        // Launch user's default browser
        Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true
        });

        // Wait for incoming callback with 2-minute timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(linkedCts.Token);
        }
        catch
        {
            return null;
        }

        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];

        var isSuccess = string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code) && (string.IsNullOrEmpty(returnedState) || returnedState == state);

        var responseString = isSuccess
            ? "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Pulse - Authorized</title></head><body style='background:#121214;color:#FAFAFA;font-family:system-ui,sans-serif;text-align:center;padding:60px 20px;'><div style='max-width:400px;margin:0 auto;background:#1C1C20;border-radius:16px;padding:36px;border:1px solid #2C2C32;'><h2 style='margin:0 0 12px 0;color:#30D158;'>&#10003; Account Connected</h2><p style='color:#A1A1AA;font-size:14px;line-height:1.5;'>Your Google Antigravity account is now connected to Pulse. You can close this window.</p></div></body></html>"
            : "<!DOCTYPE html><html><body style='background:#121214;color:#EF4444;font-family:sans-serif;text-align:center;padding:50px;'><h2>Authorization Failed</h2><p>Please try again in Pulse.</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(responseString);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(buffer, linkedCts.Token);
        context.Response.OutputStream.Close();

        if (!isSuccess || string.IsNullOrEmpty(code)) return null;

        // Exchange code for tokens
        var (accessToken, refreshToken) = await ExchangeCodeForTokensAsync(code, redirectUri, linkedCts.Token);
        if (string.IsNullOrEmpty(accessToken)) return null;

        // Get user profile email
        var email = await FetchUserEmailAsync(accessToken, linkedCts.Token);
        if (string.IsNullOrEmpty(email)) return null;

        var accountId = $"antigravity#{email.Trim().ToLowerInvariant()}";
        _credentials.SetAccountToken(accountId, accessToken);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            _credentials.SetAccountToken($"{accountId}:refresh", refreshToken);
        }

        return email.Trim().ToLowerInvariant();
    }

    private async Task<(string? accessToken, string? refreshToken)> ExchangeCodeForTokensAsync(string code, string redirectUri, CancellationToken ct)
    {
        var pairs = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };

        using var content = new FormUrlEncodedContent(pairs);
        using var res = await _http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        if (!res.IsSuccessStatusCode) return (null, null);

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var at = root.TryGetProperty("access_token", out var aProp) ? aProp.GetString() : null;
        var rt = root.TryGetProperty("refresh_token", out var rProp) ? rProp.GetString() : null;

        return (at, rt);
    }

    private async Task<string?> FetchUserEmailAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("email", out var emailProp))
        {
            return emailProp.GetString();
        }

        return null;
    }
}
