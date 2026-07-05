using System.Security.Cryptography;
using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

public class MasterPasswordServiceTests
{
    private const string OldPassword = "StrongTestPassword123!";
    private const string NewPassword = "AnotherStrongPassword456!";
    private const string ThirdPassword = "YetAnotherStrongPass789!";
    private const string WrongPassword = "_wrongPassword000!";

    private const string WrapMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Passphrase = "Coffer";

    [Fact]
    public async Task Change_WithCorrectCurrentPassword_RotatesSoNewLogsInAndOldFails()
    {
        using var paths = new TestVaultPaths();
        await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var keyVault = new InMemoryKeyVault();
        var service = CreateService(paths, keyVault);

        await service.ChangeMasterPasswordAsync(OldPassword, NewPassword, CancellationToken.None);

        var newLogin = NewLoginService(paths);
        await newLogin.LoginWithPasswordAsync(NewPassword, CancellationToken.None);

        var oldLogin = NewLoginService(paths);
        var act = async () => await oldLogin.LoginWithPasswordAsync(OldPassword, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidMasterPasswordException>();
    }

    [Fact]
    public async Task Change_WithWrongCurrentPassword_ThrowsAndLeavesFileUnchanged()
    {
        using var paths = new TestVaultPaths();
        await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var before = await File.ReadAllBytesAsync(paths.EncryptedDekFilePath);
        var service = CreateService(paths, new InMemoryKeyVault());

        var act = async () => await service.ChangeMasterPasswordAsync(WrongPassword, NewPassword, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMasterPasswordException>();
        (await File.ReadAllBytesAsync(paths.EncryptedDekFilePath)).Should().Equal(before,
            "a rejected change must not rewrite the DEK file");
    }

    [Fact]
    public async Task Change_OnV2Vault_PreservesSeedBlob_SeedStillRecovers()
    {
        using var paths = new TestVaultPaths();
        var dek = await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var service = CreateService(paths, new InMemoryKeyVault());

        await service.ChangeMasterPasswordAsync(OldPassword, NewPassword, CancellationToken.None);

        var file = await DekFile.ReadAsync(paths.EncryptedDekFilePath, CancellationToken.None);
        file.Version.Should().Be(DekFile.CurrentVersion, "a v2 vault stays v2");
        file.HasSeedWrap.Should().BeTrue();

        // The seed still recovers the same DEK (its blob was carried over unchanged).
        var recoverHolder = new DekHolder();
        var seedService = new SeedRecoveryService(
            new Argon2KeyDerivation(), new Bip39SeedManager(), new InMemoryKeyVault(),
            recoverHolder, paths, NullLogger<SeedRecoveryService>.Instance);
        await seedService.RecoverWithSeedAsync(WrapMnemonic, ThirdPassword, CancellationToken.None);
        recoverHolder.Get().Should().BeEquivalentTo(dek);
    }

    [Fact]
    public async Task Change_OnV1Vault_StaysV1AndNewPasswordLogsIn()
    {
        using var paths = new TestVaultPaths();
        await WriteV1DekFileAsync(paths, OldPassword);
        var service = CreateService(paths, new InMemoryKeyVault());

        await service.ChangeMasterPasswordAsync(OldPassword, NewPassword, CancellationToken.None);

        var file = await DekFile.ReadAsync(paths.EncryptedDekFilePath, CancellationToken.None);
        file.Version.Should().Be(DekFile.PasswordOnlyVersion, "a v1 vault stays v1");
        file.HasSeedWrap.Should().BeFalse();

        await NewLoginService(paths).LoginWithPasswordAsync(NewPassword, CancellationToken.None);
    }

    [Fact]
    public async Task Change_WhenNoVault_ThrowsVaultMissing()
    {
        using var paths = new TestVaultPaths();
        var service = CreateService(paths, new InMemoryKeyVault());

        var act = async () => await service.ChangeMasterPasswordAsync(OldPassword, NewPassword, CancellationToken.None);

        await act.Should().ThrowAsync<VaultMissingException>();
    }

    [Fact]
    public async Task Change_RefreshesCache_SoCachedLoginUnlocksWithTheNewKey()
    {
        using var paths = new TestVaultPaths();
        var dek = await WriteV2DekFileAsync(paths, OldPassword, WrapMnemonic);
        var keyVault = new InMemoryKeyVault();
        var service = CreateService(paths, keyVault);

        await service.ChangeMasterPasswordAsync(OldPassword, NewPassword, CancellationToken.None);

        var holder = new DekHolder();
        var login = new LoginService(
            new Argon2KeyDerivation(), keyVault, holder, paths, NullLogger<LoginService>.Instance);
        var cached = await login.TryLoginFromCachedKeyAsync(CancellationToken.None);

        cached.Should().BeTrue("the cache was refreshed with the new master key");
        holder.Get().Should().BeEquivalentTo(dek);
    }

    private static MasterPasswordService CreateService(TestVaultPaths paths, InMemoryKeyVault keyVault) =>
        new(new Argon2KeyDerivation(), keyVault, paths, NullLogger<MasterPasswordService>.Instance);

    private static LoginService NewLoginService(TestVaultPaths paths) =>
        new(new Argon2KeyDerivation(), new InMemoryKeyVault(), new DekHolder(), paths,
            NullLogger<LoginService>.Instance);

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
