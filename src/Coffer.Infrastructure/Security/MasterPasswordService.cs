using System.Security.Cryptography;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Implements master-password rotation (doc 09). Verifies the current password by decrypting the
/// password-wrapped DEK (which also yields the DEK), re-wraps that same DEK under a new Argon2 key, and
/// rewrites <c>dek.encrypted</c> preserving its version and seed blob — so the database, the DEK, and the
/// seed channel are untouched. Refreshes the DPAPI cache to the new key. Zeroes every key buffer; logs only
/// the outcome (hard rule #6).
/// </summary>
public sealed class MasterPasswordService : IMasterPasswordService
{
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromDays(7);

    private readonly IMasterKeyDerivation _keyDerivation;
    private readonly IKeyVault _keyVault;
    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<MasterPasswordService> _logger;

    public MasterPasswordService(
        IMasterKeyDerivation keyDerivation,
        IKeyVault keyVault,
        IVaultPaths vaultPaths,
        ILogger<MasterPasswordService> logger)
    {
        ArgumentNullException.ThrowIfNull(keyDerivation);
        ArgumentNullException.ThrowIfNull(keyVault);
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);

        _keyDerivation = keyDerivation;
        _keyVault = keyVault;
        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    public async Task ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var path = _vaultPaths.EncryptedDekFilePath;
        if (!File.Exists(path))
        {
            throw new VaultMissingException(path);
        }

        DekFile file;
        try
        {
            file = await DekFile.ReadAsync(path, ct).ConfigureAwait(false);
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

        byte[]? oldKey = null;
        byte[]? dek = null;
        byte[]? newKey = null;
        try
        {
            oldKey = await _keyDerivation
                .DeriveMasterKeyAsync(currentPassword, file.Salt, file.ArgonParameters, ct)
                .ConfigureAwait(false);

            try
            {
                dek = AesGcmCrypto.Decrypt(file.Ciphertext, file.Iv, file.Tag, oldKey);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning("Password change failed — the current password is invalid");
                throw new InvalidMasterPasswordException(ex);
            }

            var parameters = Argon2Parameters.Default;
            var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
            newKey = await _keyDerivation
                .DeriveMasterKeyAsync(newPassword, salt, parameters, ct)
                .ConfigureAwait(false);

            var wrap = AesGcmCrypto.Encrypt(dek, newKey);
            try
            {
                // Preserve the file version and the seed blob — only the password wrap changes.
                var updated = new DekFile(
                    Version: file.Version,
                    ArgonParameters: parameters,
                    Salt: salt,
                    Iv: wrap.Iv,
                    Tag: wrap.Tag,
                    Ciphertext: wrap.Ciphertext,
                    SeedIv: file.SeedIv,
                    SeedTag: file.SeedTag,
                    SeedCiphertext: file.SeedCiphertext);
                await DekFile.WriteReplaceAsync(updated, path, ct).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(wrap.Ciphertext, 0, wrap.Ciphertext.Length);
                Array.Clear(wrap.Tag, 0, wrap.Tag.Length);
                Array.Clear(wrap.Iv, 0, wrap.Iv.Length);
            }

            // The owner just re-authenticated — refresh the cache to the new key so cold start stays silent.
            try
            {
                await _keyVault.SetCachedMasterKeyAsync(newKey, _cacheTtl, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Master password changed but DPAPI cache write failed");
            }

            _logger.LogInformation("Master password changed");
        }
        finally
        {
            ClearIfNotNull(oldKey);
            ClearIfNotNull(newKey);
            ClearIfNotNull(dek);
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
