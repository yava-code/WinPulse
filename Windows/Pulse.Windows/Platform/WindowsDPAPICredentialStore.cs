namespace Pulse.Windows.Platform;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;
using Pulse.Core.Security;

public sealed class WindowsDPAPICredentialStore : ICredentialStore
{
    private readonly IPlatformPaths _paths;
    private readonly byte[] _keyEntropy = Encoding.UTF8.GetBytes("Pulse api keys");
    private readonly byte[] _accountEntropy = Encoding.UTF8.GetBytes("Pulse account logins");
    private readonly Lock _lock = new();

    public WindowsDPAPICredentialStore(IPlatformPaths paths)
    {
        _paths = paths;
        EnsureDirectory();
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_paths.PulseStorageDirectory))
        {
            Directory.CreateDirectory(_paths.PulseStorageDirectory);
        }
    }

    public string? GetKey(Provider provider)
    {
        lock (_lock)
        {
            var dict = LoadDictionary("keys.dat", _keyEntropy);
            return dict.TryGetValue(provider.Id(), out var val) ? val : null;
        }
    }

    public void SetKey(Provider provider, string? key)
    {
        lock (_lock)
        {
            var dict = LoadDictionary("keys.dat", _keyEntropy);
            if (string.IsNullOrWhiteSpace(key))
            {
                dict.Remove(provider.Id());
            }
            else
            {
                dict[provider.Id()] = key.Trim();
            }
            SaveDictionary("keys.dat", dict, _keyEntropy);
        }
    }

    public string? GetAccountToken(string accountId)
    {
        lock (_lock)
        {
            var dict = LoadDictionary("accounts.dat", _accountEntropy);
            return dict.TryGetValue(accountId, out var val) ? val : null;
        }
    }

    public void SetAccountToken(string accountId, string? token)
    {
        lock (_lock)
        {
            var dict = LoadDictionary("accounts.dat", _accountEntropy);
            if (string.IsNullOrWhiteSpace(token))
            {
                dict.Remove(accountId);
            }
            else
            {
                dict[accountId] = token.Trim();
            }
            SaveDictionary("accounts.dat", dict, _accountEntropy);
        }
    }

    public IReadOnlyList<string> ListAccountIds()
    {
        lock (_lock)
        {
            var dict = LoadDictionary("accounts.dat", _accountEntropy);
            return dict.Keys.ToList();
        }
    }

    private Dictionary<string, string> LoadDictionary(string filename, byte[] entropy)
    {
        var path = Path.Combine(_paths.PulseStorageDirectory, filename);
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void SaveDictionary(string filename, Dictionary<string, string> dict, byte[] entropy)
    {
        EnsureDirectory();
        var path = Path.Combine(_paths.PulseStorageDirectory, filename);

        try
        {
            var json = JsonSerializer.Serialize(dict);
            var plain = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);

            var temp = path + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Logging or fallback
        }
    }
}
