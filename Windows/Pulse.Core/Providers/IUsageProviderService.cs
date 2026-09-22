namespace Pulse.Core.Providers;

using Pulse.Core.Models;

public interface IUsageProviderService
{
    Provider Provider { get; }
    Task<ProviderUsage> FetchAsync(AccountKey account, CancellationToken cancellationToken = default);
}
