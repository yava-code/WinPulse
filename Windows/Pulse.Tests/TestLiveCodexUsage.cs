namespace Pulse.Tests;

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class TestLiveCodexUsage
{
    private readonly ITestOutputHelper _output;

    public TestLiveCodexUsage(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FetchLiveCodexResponse()
    {
        var authPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
        _output.WriteLine($"Auth path: {authPath} (Exists: {File.Exists(authPath)})");
        if (!File.Exists(authPath)) return;

        var json = File.ReadAllText(authPath);
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("tokens").GetProperty("access_token").GetString()!;
        var accountId = doc.RootElement.GetProperty("tokens").TryGetProperty("account_id", out var a) ? a.GetString() : null;

        using var client = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(accountId))
        {
            req.Headers.Add("ChatGPT-Account-Id", accountId);
        }

        var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        _output.WriteLine($"Codex HTTP Status: {res.StatusCode}");
        _output.WriteLine($"Codex HTTP Body: {body}");
    }
}
