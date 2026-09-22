namespace Pulse.Core.Models;

public readonly record struct AccountKey : IEquatable<AccountKey>
{
    public Provider Provider { get; }
    public string Id { get; }

    public AccountKey(Provider provider)
    {
        Provider = provider;
        Id = provider.Id();
    }

    public AccountKey(Provider provider, string id)
    {
        Provider = provider;
        Id = id;
    }

    public bool IsPrimary => Id == Provider.Id();

    public override string ToString() => Id;

    public static AccountKey? Parse(string storage)
    {
        if (string.IsNullOrWhiteSpace(storage)) return null;

        var parts = storage.Split('#', 2);
        var provider = ProviderExtensions.FromId(parts[0]);
        if (provider == null) return null;

        return new AccountKey(provider.Value, storage);
    }
}
