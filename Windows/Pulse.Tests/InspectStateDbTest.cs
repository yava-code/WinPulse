namespace Pulse.Tests;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

public class InspectStateDbTest
{
    private readonly ITestOutputHelper _output;

    public InspectStateDbTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestDecryptAntigravitySecrets()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localStatePath = Path.Combine(appData, "Antigravity IDE", "Local State");
        if (!File.Exists(localStatePath))
        {
            _output.WriteLine("Local State not found");
            return;
        }

        var localStateJson = File.ReadAllText(localStatePath);
        using var doc = JsonDocument.Parse(localStateJson);
        var b64Key = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!;
        var rawKey = Convert.FromBase64String(b64Key);

        // Strip "DPAPI" prefix (5 bytes)
        var encryptedMasterKey = rawKey[5..];
        var masterKey = ProtectedData.Unprotect(encryptedMasterKey, null, DataProtectionScope.CurrentUser);
        _output.WriteLine($"Decrypted Master Key length: {masterKey.Length}");

        var dbPath = Path.Combine(appData, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var conn = new SqliteConnection(connStr);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE 'secret://%'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var k = reader.GetString(0);
            var v = reader.GetString(1);
            if (!k.Contains("ag.account.")) continue;

            using var vDoc = JsonDocument.Parse(v);
            var dataArr = vDoc.RootElement.GetProperty("data");
            var bytes = new byte[dataArr.GetArrayLength()];
            int i = 0;
            foreach (var b in dataArr.EnumerateArray())
            {
                bytes[i++] = b.GetByte();
            }

            // Electron v10 format:
            // 0..3: "v10" (3 bytes)
            // 3..15: 12-byte IV/nonce
            // 15..^16: ciphertext
            // ^16..: 16-byte tag
            var iv = bytes[3..15];
            var ciphertext = bytes[15..^16];
            var tag = bytes[^16..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(masterKey, 16);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            var decrypted = Encoding.UTF8.GetString(plaintext);
            _output.WriteLine($"SUCCESS DECRYPT FOR: {k}");
            _output.WriteLine($"DECRYPTED JSON: {decrypted}");
        }
    }
}
