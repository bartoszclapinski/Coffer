using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class InMemoryKeyVaultTests
{
    [Fact]
    public async Task GetCachedMasterKeyAsync_WhenEmpty_ReturnsNull()
    {
        var vault = new InMemoryKeyVault();

        var result = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_RoundTrip_ReturnsSameBytes()
    {
        var vault = new InMemoryKeyVault();
        var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        await vault.SetCachedMasterKeyAsync(original, TimeSpan.FromMinutes(5), CancellationToken.None);
        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved.Should().Equal(original);
    }

    [Fact]
    public async Task Get_AfterTtlExpired_ReturnsNull()
    {
        var vault = new InMemoryKeyVault();
        await vault.SetCachedMasterKeyAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await Task.Delay(150);

        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task Invalidate_ThenGet_ReturnsNull()
    {
        var vault = new InMemoryKeyVault();
        await vault.SetCachedMasterKeyAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), CancellationToken.None);

        await vault.InvalidateMasterKeyCacheAsync(CancellationToken.None);
        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task Set_OverwritesPreviousKey()
    {
        var vault = new InMemoryKeyVault();
        var first = new byte[] { 1, 1, 1, 1 };
        var second = new byte[] { 2, 2, 2, 2 };

        await vault.SetCachedMasterKeyAsync(first, TimeSpan.FromMinutes(5), CancellationToken.None);
        await vault.SetCachedMasterKeyAsync(second, TimeSpan.FromMinutes(5), CancellationToken.None);
        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().Equal(second);
    }
}
