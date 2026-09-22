namespace Pulse.Core.Services;

using System.Text.Json;
using Pulse.Core.Models;
using Pulse.Core.Platform;

public sealed class UsageCache
{
    private readonly IPlatformPaths _paths;
    private readonly string _cacheFile;

    public UsageCache(IPlatformPaths paths)
    {
        _paths = paths;
        _cacheFile = Path.Combine(_paths.PulseStorageDirectory, "cache.json");
    }

    public void Save(IReadOnlyDictionary<AccountKey, ProviderUsage> readings)
    {
        try
        {
            if (!Directory.Exists(_paths.PulseStorageDirectory))
            {
                Directory.CreateDirectory(_paths.PulseStorageDirectory);
            }

            var json = UsageReport.GenerateJson(readings);
            var temp = _cacheFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _cacheFile, overwrite: true);
        }
        catch
        {
            // Non-critical cache write failure
        }
    }

    public string? ReadRaw()
    {
        if (!File.Exists(_cacheFile)) return null;
        try
        {
            return File.ReadAllText(_cacheFile);
        }
        catch
        {
            return null;
        }
    }
}
