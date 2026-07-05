using System.Security.Cryptography;
using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

/// <summary>
/// Full integration coverage of <see cref="LoginService"/> — happy paths, cache
/// path, error mapping, logout. Sprint-6 chore #45 unblocked these tests by
/// making <see cref="IVaultPaths"/> injectable, so we can write a real
/// <c>dek.encrypted</c> to a temp directory without touching the developer's
/// <c>%LocalAppData%\Coffer\</c>.
/// </summary>
public class LoginServiceTests
{
    private const string CorrectPassword = "StrongTestPassword123!";
    private const string WrongPassword = "_wrongPassword456!";

    [Fact]
    public async Task LoginWithPasswordAsync_With_correctPassword_PublishesDekAndRefreshesCache()
    {
        using var paths = new TestVaultPaths();
        var (originalDek, _) = await WriteValidDekFileAsync(paths, CorrectPassword);
        var holder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        var service = CreateService(paths, holder, keyVault);

        await service.LoginWithPasswordAsync(CorrectPassword, CancellationToken.None);

        holder.IsAvailable.Should().BeTrue();
        holder.Get().Should().BeEquivalentTo(originalDek);
        var cachedKey = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        cachedKey.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginWithPasswordAsync_With_wrongPassword_ThrowsInvalidMasterPasswordException()
    {
        using var paths = new TestVaultPaths();
        await WriteValidDekFileAsync(paths, CorrectPassword);
        var holder = new DekHolder();
        var service = CreateService(paths, holder, new InMemoryKeyVault());

        var act = async () => await service.LoginWithPasswordAsync(WrongPassword, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMasterPasswordException>();
        holder.IsAvailable.Should().BeFalse(
            "no DEK should be published when the password is wrong");
    }

    [Fact]
    public async Task LoginWithPasswordAsync_WhenDekFileMissing_ThrowsVaultMissingException()
    {
        using var paths = new TestVaultPaths();
        // No DEK file written — temp dir is empty.
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        var act = async () => await service.LoginWithPasswordAsync(CorrectPassword, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<VaultMissingException>();
        thrown.Which.FilePath.Should().Be(paths.EncryptedDekFilePath);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_WhenDekFileCorrupted_ThrowsVaultCorruptedException()
    {
        using var paths = new TestVaultPaths();
        // Write garbage that DekFile.ReadAsync cannot parse.
        await File.WriteAllBytesAsync(paths.EncryptedDekFilePath, new byte[] { 0xFF, 0xFF, 0xFF });
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        var act = async () => await service.LoginWithPasswordAsync(CorrectPassword, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<VaultCorruptedException>();
        thrown.Which.Reason.Should().Be(VaultCorruptionReason.DekFileFormat);
    }

    [Fact]
    public async Task TryLoginFromCachedKeyAsync_WithValidCache_ReturnsTrueAndPublishesDek()
    {
        using var paths = new TestVaultPaths();
        var (originalDek, masterKey) = await WriteValidDekFileAsync(paths, CorrectPassword);
        var holder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        await keyVault.SetCachedMasterKeyAsync(masterKey, TimeSpan.FromDays(7), CancellationToken.None);
        var service = CreateService(paths, holder, keyVault);

        var result = await service.TryLoginFromCachedKeyAsync(CancellationToken.None);

        result.Should().BeTrue();
        holder.IsAvailable.Should().BeTrue();
        holder.Get().Should().BeEquivalentTo(originalDek);
    }

    [Fact]
    public async Task TryLoginFromCachedKeyAsync_WhenCacheReturnsNull_ReturnsFalse()
    {
        using var paths = new TestVaultPaths();
        await WriteValidDekFileAsync(paths, CorrectPassword);
        var holder = new DekHolder();
        var service = CreateService(paths, holder, new InMemoryKeyVault()); // empty cache

        var result = await service.TryLoginFromCachedKeyAsync(CancellationToken.None);

        result.Should().BeFalse();
        holder.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task TryLoginFromCachedKeyAsync_WhenCachedKeyDoesNotDecryptDek_ReturnsFalseAndInvalidatesCache()
    {
        using var paths = new TestVaultPaths();
        await WriteValidDekFileAsync(paths, CorrectPassword);
        var holder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        var wrongMasterKey = new byte[32];
        RandomNumberGenerator.Fill(wrongMasterKey);
        await keyVault.SetCachedMasterKeyAsync(wrongMasterKey, TimeSpan.FromDays(7), CancellationToken.None);
        var service = CreateService(paths, holder, keyVault);

        var result = await service.TryLoginFromCachedKeyAsync(CancellationToken.None);

        result.Should().BeFalse();
        holder.IsAvailable.Should().BeFalse();
        var stillCached = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        stillCached.Should().BeNull(
            "a cached key that cannot unlock the DEK is stale and should be invalidated");
    }

    [Fact]
    public async Task TryLoginFromCachedKeyAsync_WhenDekFileMissing_ReturnsFalseWithoutThrowing()
    {
        using var paths = new TestVaultPaths();
        var holder = new DekHolder();
        var service = CreateService(paths, holder, new InMemoryKeyVault());

        var result = await service.TryLoginFromCachedKeyAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_ClearsHolderAndInvalidatesCache()
    {
        using var paths = new TestVaultPaths();
        var holder = new DekHolder();
        holder.Set(new byte[32]);
        var keyVault = new InMemoryKeyVault();
        await keyVault.SetCachedMasterKeyAsync(new byte[32], TimeSpan.FromMinutes(5), CancellationToken.None);
        var service = CreateService(paths, holder, keyVault);

        await service.LogoutAsync(CancellationToken.None);

        holder.IsAvailable.Should().BeFalse();
        var cached = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        cached.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_WhenCacheInvalidationThrows_StillClearsHolder()
    {
        using var paths = new TestVaultPaths();
        var holder = new DekHolder();
        holder.Set(new byte[32]);
        var keyVault = new ThrowingKeyVault();
        var service = CreateService(paths, holder, keyVault);

        var act = async () => await service.LogoutAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            "LogoutAsync must be defensive — a partial logout still beats a hung MainWindow");
        holder.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task LoginWithPasswordAsync_WithNullPassword_ThrowsArgumentNullException()
    {
        using var paths = new TestVaultPaths();
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        var act = async () => await service.LoginWithPasswordAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Builds a valid <c>dek.encrypted</c> at the supplied paths using the same
    /// primitives <see cref="SetupService"/> uses on the production write path:
    /// Argon2id key derivation, AES-GCM-encrypted DEK, <see cref="DekFile.WriteAsync"/>.
    /// Returns the cleartext DEK and master key so individual tests can assert on
    /// the round-trip values.
    /// </summary>
    private static async Task<(byte[] dek, byte[] masterKey)> WriteValidDekFileAsync(
        TestVaultPaths paths, string password)
    {
        var parameters = Argon2Parameters.Default;
        var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
        var derivation = new Argon2KeyDerivation();
        var masterKey = await derivation.DeriveMasterKeyAsync(
            password, salt, parameters, CancellationToken.None);

        var dek = RandomNumberGenerator.GetBytes(32);
        var encryption = AesGcmCrypto.Encrypt(dek, masterKey);

        // A legacy password-only (v1) vault — LoginService must still open it after dual-wrap shipped.
        var file = new DekFile(
            DekFile.PasswordOnlyVersion,
            parameters,
            salt,
            encryption.Iv,
            encryption.Tag,
            encryption.Ciphertext);
        await DekFile.WriteAsync(file, paths.EncryptedDekFilePath, CancellationToken.None);

        return (dek, masterKey);
    }

    private static LoginService CreateService(
        IVaultPaths paths,
        IDekHolder holder,
        IKeyVault keyVault) =>
        new(
            new Argon2KeyDerivation(),
            keyVault,
            holder,
            paths,
            NullLogger<LoginService>.Instance);

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
