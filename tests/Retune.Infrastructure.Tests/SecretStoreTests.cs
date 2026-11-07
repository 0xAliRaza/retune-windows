using Retune.Infrastructure.Secrets;

namespace Retune.Infrastructure.Tests;

/// <summary>Runs against the real Credential Manager under a unique test prefix, cleaned up afterwards.</summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly CredentialManagerSecretStore _store = new($"RetuneTest-{Guid.NewGuid():N}/");
    private readonly Guid _id = Guid.NewGuid();

    public void Dispose() => _store.Remove(_id);

    [Fact]
    public void Set_update_get_and_remove()
    {
        Assert.Null(_store.Find(_id));

        _store.Store(_id, "first");
        Assert.Equal("first", _store.Find(_id));

        _store.Store(_id, "p@ss wörd/+&=😀");
        Assert.Equal("p@ss wörd/+&=😀", _store.Find(_id));

        _store.Remove(_id);
        Assert.Null(_store.Find(_id));
        _store.Remove(_id);
    }

    [Fact]
    public void An_empty_secret_round_trips()
    {
        _store.Store(_id, string.Empty);

        Assert.Equal(string.Empty, _store.Find(_id));
    }
}
