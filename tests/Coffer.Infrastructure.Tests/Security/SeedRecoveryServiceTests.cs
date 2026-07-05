using System.Security.Cryptography;
using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

public class SeedRecoveryServiceTests
{
    private const string WrapMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    // A different, also-valid BIP39 mnemonic (classic 0x7f… test vector) — the "wrong seed" case.
    private const string OtherMnemonic =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private const string OldPassword = "StrongTestPassword123!";
    private const string NewPassword = "AnotherStrongPassword456!";

    // Must match VaultSeedDerivation.Passphrase; pinned here so a drift breaks recovery in these tests.
    private const string Passphrase = "Coffer";

    [Fact]
    public async Task RecoverWithSeed_WithCorrectSeed_ResetsPasswordAndPublishesDek()
    {
        using var paths = new TestVaultPaths();
        var dek = await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var holder = new DekHolder();
        var service = CreateService(paths, holder, new InMemoryKeyVault());

        await service.RecoverWithSeedAsync(WrapMnemonic, NewPassword, CancellationToken.None);

        holder.IsAvailable.Should().BeTrue();
        holder.Get().Should().BeEquivalentTo(dek, "recovery unlocks the original DEK");

        // The new password now logs in; the old one no longer does.
        var login = new LoginService(
            new Argon2KeyDerivation(), new InMemoryKeyVault(), new DekHolder(), paths,
            NullLogger<LoginService>.Instance);
        await login.LoginWithPasswordAsync(NewPassword, CancellationToken.None);

        var oldLogin = new LoginService(
            new Argon2KeyDerivation(), new InMemoryKeyVault(), new DekHolder(), paths,
            NullLogger<LoginService>.Instance);
        var act = async () => await oldLogin.LoginWithPasswordAsync(OldPassword, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidMasterPasswordException>();

        // The seed is carried over — still enabled after the reset.
        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task RecoverWithSeed_WithWrongSeed_ThrowsAndPublishesNothing()
    {
        using var paths = new TestVaultPaths();
        await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var holder = new DekHolder();
        var service = CreateService(paths, holder, new InMemoryKeyVault());

        var act = async () => await service.RecoverWithSeedAsync(OtherMnemonic, NewPassword, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecoverySeedException>();
        holder.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task RecoverWithSeed_OnLegacyV1Vault_ThrowsSeedRecoveryUnavailable()
    {
        using var paths = new TestVaultPaths();
        await WriteV1DekFileAsync(paths, OldPassword);
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        var act = async () => await service.RecoverWithSeedAsync(WrapMnemonic, NewPassword, CancellationToken.None);

        await act.Should().ThrowAsync<SeedRecoveryUnavailableException>();
    }

    [Fact]
    public async Task RecoverWithSeed_WhenNoVault_ThrowsVaultMissing()
    {
        using var paths = new TestVaultPaths();
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        var act = async () => await service.RecoverWithSeedAsync(WrapMnemonic, NewPassword, CancellationToken.None);

        await act.Should().ThrowAsync<VaultMissingException>();
    }

    [Fact]
    public async Task EnableSeedRecovery_OnV1Vault_UpgradesToV2AndTheSeedWorks()
    {
        using var paths = new TestVaultPaths();
        var dek = await WriteV1DekFileAsync(paths, OldPassword);
        var holder = new DekHolder();
        holder.Set(dek);
        var service = CreateService(paths, holder, new InMemoryKeyVault());

        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeFalse();

        await service.EnableSeedRecoveryAsync(WrapMnemonic, CancellationToken.None);

        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeTrue();

        // The just-enabled seed now recovers the same DEK.
        var recoverHolder = new DekHolder();
        var recoverService = CreateService(paths, recoverHolder, new InMemoryKeyVault());
        await recoverService.RecoverWithSeedAsync(WrapMnemonic, NewPassword, CancellationToken.None);
        recoverHolder.Get().Should().BeEquivalentTo(dek);
    }

    [Fact]
    public async Task EnableSeedRecovery_WhenLocked_Throws()
    {
        using var paths = new TestVaultPaths();
        await WriteV1DekFileAsync(paths, OldPassword);
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault()); // holder empty = locked

        var act = async () => await service.EnableSeedRecoveryAsync(WrapMnemonic, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IsSeedRecoveryEnabled_ReflectsVaultState()
    {
        using var paths = new TestVaultPaths();
        var service = CreateService(paths, new DekHolder(), new InMemoryKeyVault());

        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeFalse("no vault");

        await WriteV1DekFileAsync(paths, OldPassword);
        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeFalse("v1 vault");

        File.Delete(paths.EncryptedDekFilePath);
        await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        (await service.IsSeedRecoveryEnabledAsync(CancellationToken.None)).Should().BeTrue("v2 vault");
    }

    private static SeedRecoveryService CreateService(TestVaultPaths paths, DekHolder holder, InMemoryKeyVault keyVault) =>
        new(
            new Argon2KeyDerivation(),
            new Bip39SeedManager(),
            keyVault,
            holder,
            paths,
            NullLogger<SeedRecoveryService>.Instance);

    private static async Task<byte[]> WriteV2DekFileAsync(TestVaultPaths paths, string password, string mnemonic)
    {
        var parameters = Argon2Parameters.Default;
        var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
        var masterKey = await new Argon2KeyDerivation()
            .DeriveMasterKeyAsync(password, salt, parameters, CancellationToken.None);
        var recoveryKey = await new Bip39SeedManager()
            .DeriveRecoveryKeyAsync(mnemonic, Passphrase, CancellationToken.None);

        var dek = RandomNumberGenerator.GetBytes(32);
        var pw = AesGcmCrypto.Encrypt(dek, masterKey);
        var seed = AesGcmCrypto.Encrypt(dek, recoveryKey);

        var file = new DekFile(
            DekFile.CurrentVersion, parameters, salt,
            pw.Iv, pw.Tag, pw.Ciphertext,
            seed.Iv, seed.Tag, seed.Ciphertext);
        await DekFile.WriteAsync(file, paths.EncryptedDekFilePath, CancellationToken.None);
        return dek;
    }

    private static async Task<byte[]> WriteV1DekFileAsync(TestVaultPaths paths, string password)
    {
        var parameters = Argon2Parameters.Default;
        var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
        var masterKey = await new Argon2KeyDerivation()
            .DeriveMasterKeyAsync(password, salt, parameters, CancellationToken.None);

        var dek = RandomNumberGenerator.GetBytes(32);
        var pw = AesGcmCrypto.Encrypt(dek, masterKey);

        var file = new DekFile(DekFile.PasswordOnlyVersion, parameters, salt, pw.Iv, pw.Tag, pw.Ciphertext);
        await DekFile.WriteAsync(file, paths.EncryptedDekFilePath, CancellationToken.None);
        return dek;
    }
}
