using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class Argon2KeyDerivationTests
{
    private static readonly Argon2Parameters _fastParameters = new(
        MemorySizeKb: 8192,
        Iterations: 1,
        Parallelism: 1,
        OutputBytes: 32,
        SaltBytes: 16);

    [Fact]
    public async Task Derive_WithSamePasswordAndSalt_ProducesSameKey()
    {
        var derivation = new Argon2KeyDerivation();
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var first = await derivation.DeriveMasterKeyAsync("password", salt, _fastParameters, CancellationToken.None);
        var second = await derivation.DeriveMasterKeyAsync("password", salt, _fastParameters, CancellationToken.None);

        first.Should().Equal(second);
    }

    [Fact]
    public async Task Derive_WithDifferentSalts_ProducesDifferentKeys()
    {
        var derivation = new Argon2KeyDerivation();
        var salt1 = new byte[16];
        var salt2 = new byte[16];
        salt1[0] = 1;
        salt2[0] = 2;

        var first = await derivation.DeriveMasterKeyAsync("password", salt1, _fastParameters, CancellationToken.None);
        var second = await derivation.DeriveMasterKeyAsync("password", salt2, _fastParameters, CancellationToken.None);

        first.Should().NotEqual(second);
    }

    [Fact]
    public async Task Derive_OutputBytes_MatchesParametersOutputBytes()
    {
        var derivation = new Argon2KeyDerivation();
        var salt = new byte[16];
        var parameters = _fastParameters with { OutputBytes = 48 };

        var key = await derivation.DeriveMasterKeyAsync("password", salt, parameters, CancellationToken.None);

        key.Should().HaveCount(48);
    }

    [Fact]
    public async Task Derive_CanBeCancelled_BeforeStart_Throws()
    {
        var derivation = new Argon2KeyDerivation();
        var salt = new byte[16];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await derivation.DeriveMasterKeyAsync("password", salt, _fastParameters, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
