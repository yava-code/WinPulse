namespace Pulse.Tests;

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

public class TestLiveAntigravityQuota
{
    private readonly ITestOutputHelper _output;

    public TestLiveAntigravityQuota(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FetchLiveQuotasForAllAntigravityAccounts()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localStatePath = Path.Combine(appData, "Antigravity IDE", "Local State");
        if (!File.Exists(localStatePath)) return;

        var localStateJson = File.ReadAllText(localStatePath);
        using var doc = JsonDocument.Parse(localStateJson);
        var b64Key = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!;
        var rawKey = Convert.FromBase64String(b64Key);
        var masterKey = ProtectedData.Unprotect(rawKey[5..], null, DataProtectionScope.CurrentUser);

        var dbPath = Path.Combine(appData, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE 'secret://%ag.account.%'";
        using var reader = cmd.ExecuteReader();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Antigravity/4.1.29 Chrome/132.0.6834.160 Electron/39.2.3");

        while (reader.Read())
        {
            var k = reader.GetString(0);
            var v = reader.GetString(1);

            using var vDoc = JsonDocument.Parse(v);
            var dataArr = vDoc.RootElement.GetProperty("data");
            var bytes = new byte[dataArr.GetArrayLength()];
            int i = 0;
            foreach (var b in dataArr.EnumerateArray()) bytes[i++] = b.GetByte();

            var iv = bytes[3..15];
            var ciphertext = bytes[15..^16];
            var tag = bytes[^16..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(masterKey, 16);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            var tokenJson = Encoding.UTF8.GetString(plaintext);
            using var tDoc = JsonDocument.Parse(tokenJson);
            var accessToken = tDoc.RootElement.GetProperty("access_token").GetString()!;

            _output.WriteLine($"Testing account secret key: {k}");

            // 1. loadCodeAssist
            using var loadReq = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } }), Encoding.UTF8, "application/json")
            };
            loadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var loadRes = await client.SendAsync(loadReq);
            var loadBody = await loadRes.Content.ReadAsStringAsync();
            _output.WriteLine($"loadCodeAssist Status: {loadRes.StatusCode}");
            _output.WriteLine($"loadCodeAssist Body: {(loadBody.Length > 200 ? loadBody.Substring(0, 200) : loadBody)}");

            string projectId = "cloudaicompanion-enterprise";
            try
            {
                using var pDoc = JsonDocument.Parse(loadBody);
                if (pDoc.RootElement.TryGetProperty("cloudaicompanionProject", out var p))
                {
                    projectId = p.GetString() ?? projectId;
                }
            }
            catch { }

            // 2. retrieveUserQuota
            using var quotaReq = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { project = projectId }), Encoding.UTF8, "application/json")
            };
            quotaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var quotaRes = await client.SendAsync(quotaReq);
            var quotaBody = await quotaRes.Content.ReadAsStringAsync();
            _output.WriteLine($"retrieveUserQuota Status: {quotaRes.StatusCode}");
            _output.WriteLine($"retrieveUserQuota Body: {quotaBody}");
        }
    }
}
