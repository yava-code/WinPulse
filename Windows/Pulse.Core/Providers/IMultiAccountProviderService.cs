namespace Pulse.Core.Providers;

using Pulse.Core.Models;

public interface IMultiAccountProviderService : IUsageProviderService
{
    Task<IReadOnlyList<AccountKey>> DiscoverAccountsAsync(CancellationToken cancellationToken = default);
}
