using System.Security.Cryptography;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Implements the BIP39-seed channel of the dual-wrapped DEK (doc 08 "Restore from seed", doc 09). Mirrors
/// <see cref="LoginService"/> in style: read the DEK file, derive the recovery key, AES-GCM-decrypt the
/// seed-wrapped DEK, re-wrap under a new master key, rewrite the DEK file atomically, publish via
/// <see cref="IDekHolder"/>, and zero every key buffer. Never logs a seed, password, or key (hard rule #6).
/// </summary>
public sealed class SeedRecoveryService : ISeedRecoveryService
{
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromDays(7);

    private readonly IMasterKeyDerivation _keyDerivation;
    private readonly ISeedManager _seedManager;
    private readonly IKeyVault _keyVault;
    private readonly IDekHolder _dekHolder;
    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<SeedRecoveryService> _logger;

    public SeedRecoveryService(
        IMasterKeyDerivation keyDerivation,
        ISeedManager seedManager,
        IKeyVault keyVault,
        IDekHolder dekHolder,
        IVaultPaths vaultPaths,
        ILogger<SeedRecoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(keyDerivation);
        ArgumentNullException.ThrowIfNull(seedManager);
        ArgumentNullException.ThrowIfNull(keyVault);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);

        _keyDerivation = keyDerivation;
        _seedManager = seedManager;
        _keyVault = keyVault;
        _dekHolder = dekHolder;
        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    public async Task<bool> IsSeedRecoveryEnabledAsync(CancellationToken ct)
    {
        var path = _vaultPaths.EncryptedDekFilePath;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var file = await DekFile.ReadAsync(path, ct).ConfigureAwait(false);
            return file.HasSeedWrap;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            _logger.LogWarning(ex, "Could not read the DEK file to determine seed-recovery state");
            return false;
        }
    }

    public async Task RecoverWithSeedAsync(string mnemonic, string newMasterPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        ArgumentNullException.ThrowIfNull(newMasterPassword);

        var file = await ReadDekFileAsync(ct).ConfigureAwait(false);
        if (!file.HasSeedWrap)
        {
            throw new SeedRecoveryUnavailableException();
        }

        byte[]? recoveryKey = null;
        byte[]? dek = null;
        byte[]? newMasterKey = null;
        try
        {
            recoveryKey = await _seedManager
                .DeriveRecoveryKeyAsync(mnemonic, VaultSeedDerivation.Passphrase, ct)
                .ConfigureAwait(false);

            try
            {
                dek = AesGcmCrypto.Decrypt(file.SeedCiphertext!, file.SeedIv!, file.SeedTag!, recoveryKey);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning("Seed recovery failed — the seed did not unlock the vault");
                throw new InvalidRecoverySeedException(ex);
            }

            // Re-wrap the DEK under a new master password. Fresh salt + current defaults; the seed blob is
            // carried over unchanged so the seed keeps working after the reset.
            var parameters = Argon2Parameters.Default;
            var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
            newMasterKey = await _keyDerivation
                .DeriveMasterKeyAsync(newMasterPassword, salt, parameters, ct)
                .ConfigureAwait(false);

            var wrap = AesGcmCrypto.Encrypt(dek, newMasterKey);
            try
            {
                var updated = new DekFile(
                    Version: DekFile.CurrentVersion,
                    ArgonParameters: parameters,
                    Salt: salt,
                    Iv: wrap.Iv,
                    Tag: wrap.Tag,
                    Ciphertext: wrap.Ciphertext,
                    SeedIv: file.SeedIv,
                    SeedTag: file.SeedTag,
                    SeedCiphertext: file.SeedCiphertext);
                await DekFile.WriteReplaceAsync(updated, _vaultPaths.EncryptedDekFilePath, ct).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(wrap.Ciphertext, 0, wrap.Ciphertext.Length);
                Array.Clear(wrap.Tag, 0, wrap.Tag.Length);
                Array.Clear(wrap.Iv, 0, wrap.Iv.Length);
            }

            _dekHolder.Set(dek);

            try
            {
                await _keyVault.SetCachedMasterKeyAsync(newMasterKey, _cacheTtl, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Seed recovery succeeded but DPAPI cache write failed");
            }

            _logger.LogInformation("Vault recovered via BIP39 seed; master password reset");
        }
        finally
        {
            ClearIfNotNull(recoveryKey);
            ClearIfNotNull(newMasterKey);
            ClearIfNotNull(dek);
        }
    }

    public async Task EnableSeedRecoveryAsync(string mnemonic, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        if (!_dekHolder.IsAvailable)
        {
            throw new InvalidOperationException("Seed recovery can only be enabled while the vault is unlocked.");
        }

        var file = await ReadDekFileAsync(ct).ConfigureAwait(false);

        byte[]? recoveryKey = null;
        var dek = _dekHolder.Get();
        try
        {
            recoveryKey = await _seedManager
                .DeriveRecoveryKeyAsync(mnemonic, VaultSeedDerivation.Passphrase, ct)
                .ConfigureAwait(false);

            var wrap = AesGcmCrypto.Encrypt(dek, recoveryKey);
            try
            {
                // Keep the existing password-wrapped blob + Argon2 params; add the seed blob.
                var updated = new DekFile(
                    Version: DekFile.CurrentVersion,
                    ArgonParameters: file.ArgonParameters,
                    Salt: file.Salt,
                    Iv: file.Iv,
                    Tag: file.Tag,
                    Ciphertext: file.Ciphertext,
                    SeedIv: wrap.Iv,
                    SeedTag: wrap.Tag,
                    SeedCiphertext: wrap.Ciphertext);
                await DekFile.WriteReplaceAsync(updated, _vaultPaths.EncryptedDekFilePath, ct).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(wrap.Ciphertext, 0, wrap.Ciphertext.Length);
                Array.Clear(wrap.Tag, 0, wrap.Tag.Length);
                Array.Clear(wrap.Iv, 0, wrap.Iv.Length);
            }

            _logger.LogInformation("Seed recovery enabled — DEK file upgraded to dual-wrap");
        }
        finally
        {
            ClearIfNotNull(recoveryKey);
            Array.Clear(dek, 0, dek.Length);
        }
    }

    private async Task<DekFile> ReadDekFileAsync(CancellationToken ct)
    {
        var path = _vaultPaths.EncryptedDekFilePath;
        if (!File.Exists(path))
        {
            throw new VaultMissingException(path);
        }

        try
        {
            return await DekFile.ReadAsync(path, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new VaultCorruptedException(
                VaultCorruptionReason.DekFileFormat,
                "The DEK file is malformed or uses an unsupported version.",
                ex);
        }
        catch (IOException ex)
        {
            throw new VaultCorruptedException(
                VaultCorruptionReason.DekFileIo,
                "An I/O error prevented reading the DEK file.",
                ex);
        }
    }

    private static void ClearIfNotNull(byte[]? buffer)
    {
        if (buffer is not null)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }
}
