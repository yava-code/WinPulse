namespace Pulse.Core.Security;

using Pulse.Core.Models;

public interface ICredentialStore
{
    string? GetKey(Provider provider);
    void SetKey(Provider provider, string? key);

    string? GetAccountToken(string accountId);
    void SetAccountToken(string accountId, string? token);
    IReadOnlyList<string> ListAccountIds();
}
