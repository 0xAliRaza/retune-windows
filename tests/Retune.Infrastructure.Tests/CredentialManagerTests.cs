namespace Retune.Infrastructure.Tests;

/// <summary>
/// Tests that use the real Windows Credential Manager run one at a time: enumerating the vault while another test
/// writes to it can briefly miss fresh entries, which made these tests flaky when run in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CredentialManagerTests
{
    public const string Name = "Credential Manager";
}