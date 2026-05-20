using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

/// <summary>
/// Tests for the logout path and the cache-miss path of <see cref="LoginService"/>.
/// The happy path (real Argon2 + AES-GCM + DEK file round-trip) is exercised manually
/// per the Sprint 6 manual-verification checklist; in-process integration is blocked
/// on the same deferred follow-up as <see cref="SetupServiceTests"/> —
/// <c>CofferPaths</c> is static and writes to <c>%LocalAppData%</c>.
/// </summary>
public class LoginServiceTests
{
    [Fact]
    public async Task LogoutAsync_ClearsHolderAndInvalidatesCache()
    {
        var holder = new DekHolder();
        holder.Set(new byte[32]);
        var keyVault = new InMemoryKeyVault();
        await keyVault.SetCachedMasterKeyAsync(new byte[32], TimeSpan.FromMinutes(5), CancellationToken.None);

        var service = new LoginService(
            new Argon2KeyDerivation(),
            keyVault,
            holder,
            NullLogger<LoginService>.Instance);

        await service.LogoutAsync(CancellationToken.None);

        holder.IsAvailable.Should().BeFalse();
        var cached = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        cached.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_WhenCacheInvalidationThrows_StillClearsHolder()
    {
        var holder = new DekHolder();
        holder.Set(new byte[32]);
        var keyVault = new ThrowingKeyVault();

        var service = new LoginService(
            new Argon2KeyDerivation(),
            keyVault,
            holder,
            NullLogger<LoginService>.Instance);

        var act = async () => await service.LogoutAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            "LogoutAsync must be defensive — a partial logout still beats a hung MainWindow");
        holder.IsAvailable.Should().BeFalse(
            "the holder must be cleared even when the cache invalidation step fails");
    }

    [Fact]
    public async Task TryLoginFromCachedKeyAsync_WhenCacheReturnsNull_ReturnsFalse()
    {
        // Cache miss returns false regardless of whether the dek.encrypted file exists
        // on disk — the missing-file branch short-circuits earlier with the same result.
        var holder = new DekHolder();
        var keyVault = new InMemoryKeyVault(); // no cached key set
        var service = new LoginService(
            new Argon2KeyDerivation(),
            keyVault,
            holder,
            NullLogger<LoginService>.Instance);

        var result = await service.TryLoginFromCachedKeyAsync(CancellationToken.None);

        result.Should().BeFalse();
        holder.IsAvailable.Should().BeFalse(
            "no DEK should be published when the cached-key login does not succeed");
    }

    [Fact]
    public async Task LoginWithPasswordAsync_WithNullPassword_ThrowsArgumentNullException()
    {
        var service = new LoginService(
            new Argon2KeyDerivation(),
            new InMemoryKeyVault(),
            new DekHolder(),
            NullLogger<LoginService>.Instance);

        var act = async () => await service.LoginWithPasswordAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class ThrowingKeyVault : IKeyVault
    {
        public Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct) =>
            Task.FromResult<byte[]?>(null);

        public Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct) =>
            Task.FromException(new InvalidOperationException("test vault always throws"));

        public Task InvalidateMasterKeyCacheAsync(CancellationToken ct) =>
            Task.FromException(new InvalidOperationException("test vault always throws"));
    }
}
