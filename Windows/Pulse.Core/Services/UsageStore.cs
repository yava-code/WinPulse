namespace Pulse.Core.Services;

using System.Collections.Concurrent;
using Pulse.Core.Models;
using Pulse.Core.Providers;

public sealed class UsageStore : IAsyncDisposable
{
    private readonly IEnumerable<IUsageProviderService> _services;
    private readonly ConcurrentDictionary<AccountKey, ProviderUsage> _readings = new();
    private readonly List<AccountKey> _monitoredAccounts = new();
    private readonly Lock _lock = new();

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _timerTask;

    public event Action<AccountKey, ProviderUsage>? UsageUpdated;
    public event Action? RefreshCompleted;

    public IReadOnlyDictionary<AccountKey, ProviderUsage> Readings => _readings;

    public UsageStore(IEnumerable<IUsageProviderService> services)
    {
        _services = services;
    }

    public void SetMonitoredAccounts(IEnumerable<AccountKey> accounts)
    {
        lock (_lock)
        {
            _monitoredAccounts.Clear();
            _monitoredAccounts.AddRange(accounts);
        }
    }

    public IReadOnlyList<AccountKey> GetMonitoredAccounts()
    {
        lock (_lock)
        {
            return _monitoredAccounts.ToList();
        }
    }

    public void Start(TimeSpan refreshInterval)
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(refreshInterval);
        _timerTask = RunLoopAsync(_timer, _cts.Token);
    }

    private async Task RunLoopAsync(PeriodicTimer timer, CancellationToken token)
    {
        await RefreshAllAsync(token);
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            await RefreshAllAsync(token);
        }
    }

    public async Task ExpandMultiAccountsAsync(CancellationToken token = default)
    {
        List<AccountKey> current;
        lock (_lock)
        {
            current = _monitoredAccounts.ToList();
        }

        var expanded = new List<AccountKey>();
        foreach (var acc in current)
        {
            var service = _services.FirstOrDefault(s => s.Provider == acc.Provider);
            if (service is IMultiAccountProviderService multi)
            {
                try
                {
                    var discovered = await multi.DiscoverAccountsAsync(token);
                    if (discovered.Count > 0)
                    {
                        foreach (var d in discovered)
                        {
                            if (!expanded.Contains(d)) expanded.Add(d);
                        }
                        continue;
                    }
                }
                catch { }
            }
            if (!expanded.Contains(acc)) expanded.Add(acc);
        }

        lock (_lock)
        {
            _monitoredAccounts.Clear();
            _monitoredAccounts.AddRange(expanded);
        }
    }

    public async Task RefreshAllAsync(CancellationToken token = default)
    {
        await ExpandMultiAccountsAsync(token);

        List<AccountKey> targets;
        lock (_lock)
        {
            targets = _monitoredAccounts.ToList();
        }

        if (targets.Count == 0)
        {
            return;
        }

        var tasks = targets.Select(async account =>
        {
            var service = _services.FirstOrDefault(s => s.Provider == account.Provider);
            if (service == null) return;

            try
            {
                var usage = await service.FetchAsync(account, token);
                _readings[account] = usage;
                UsageUpdated?.Invoke(account, usage);
            }
            catch (Exception ex)
            {
                var errorUsage = ProviderUsage.Unavailable(account, $"Error: {ex.Message}");
                _readings[account] = errorUsage;
                UsageUpdated?.Invoke(account, errorUsage);
            }
        });

        await Task.WhenAll(tasks);
        RefreshCompleted?.Invoke();
    }

    public string ExportJson() => UsageReport.GenerateJson(_readings);

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _timer?.Dispose();
            if (_timerTask != null)
            {
                try { await _timerTask; } catch { }
            }
            _cts.Dispose();
        }
    }
}
