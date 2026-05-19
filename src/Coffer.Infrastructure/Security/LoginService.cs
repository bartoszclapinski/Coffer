using System.Security.Cryptography;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Orchestrates the cached-key and password-based login paths and the logout path.
/// Mirrors <see cref="SetupService"/> in style but in reverse: read the DEK file,
/// derive (or fetch cached) master key, AES-GCM-decrypt the DEK, publish via
/// <see cref="IDekHolder"/>, refresh the DPAPI cache, zero every key buffer.
/// </summary>
public sealed class LoginService : ILoginService
{
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromDays(7);

    private readonly IMasterKeyDerivation _keyDerivation;
    private readonly IKeyVault _keyVault;
    private readonly IDekHolder _dekHolder;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        IMasterKeyDerivation keyDerivation,
        IKeyVault keyVault,
        IDekHolder dekHolder,
        ILogger<LoginService> logger)
    {
        ArgumentNullException.ThrowIfNull(keyDerivation);
        ArgumentNullException.ThrowIfNull(keyVault);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(logger);

        _keyDerivation = keyDerivation;
        _keyVault = keyVault;
        _dekHolder = dekHolder;
        _logger = logger;
    }

    public async Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct)
    {
        var dekPath = CofferPaths.EncryptedDekFilePath();
        if (!File.Exists(dekPath))
        {
            _logger.LogDebug("Cached-key login skipped — DEK file does not exist");
            return false;
        }

        byte[]? masterKey = null;
        byte[]? dek = null;
        try
        {
            masterKey = await _keyVault.GetCachedMasterKeyAsync(ct).ConfigureAwait(false);
            if (masterKey is null)
            {
                _logger.LogDebug("Cached-key login skipped — cache miss or expired");
                return false;
            }

            DekFile file;
            try
            {
                file = await DekFile.ReadAsync(dekPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or FileNotFoundException)
            {
                _logger.LogWarning(ex, "Cached-key login failed — DEK file could not be read; invalidating cache");
                await SafeInvalidateCacheAsync().ConfigureAwait(false);
                return false;
            }

            try
            {
                dek = AesGcmCrypto.Decrypt(file.Ciphertext, file.Iv, file.Tag, masterKey);
            }
            catch (CryptographicException ex)
            {
                // The cached master key cannot unlock the current DEK file. This usually
                // means the DEK was rewritten with a different master key (password
                // change in a future sprint) without invalidating the cache, OR the cache
                // payload was tampered with. Either way the cache is now useless.
                _logger.LogWarning(ex, "Cached-key login failed — AES-GCM tag mismatch; invalidating cache");
                await SafeInvalidateCacheAsync().ConfigureAwait(false);
                return false;
            }

            _dekHolder.Set(dek);
            _logger.LogInformation("Login completed via cached master key");
            return true;
        }
        finally
        {
            if (masterKey is not null)
            {
                Array.Clear(masterKey, 0, masterKey.Length);
            }
            if (dek is not null)
            {
                Array.Clear(dek, 0, dek.Length);
            }
        }
    }

    public async Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        var dekPath = CofferPaths.EncryptedDekFilePath();
        if (!File.Exists(dekPath))
        {
            throw new VaultMissingException(dekPath);
        }

        DekFile file;
        try
        {
            file = await DekFile.ReadAsync(dekPath, ct).ConfigureAwait(false);
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

        byte[]? masterKey = null;
        byte[]? dek = null;
        try
        {
            // Argon2 parameters come from the file, not Argon2Parameters.Default — old
            // vaults stay decryptable even if the application defaults change later.
            masterKey = await _keyDerivation
                .DeriveMasterKeyAsync(masterPassword, file.Salt, file.ArgonParameters, ct)
                .ConfigureAwait(false);

            try
            {
                dek = AesGcmCrypto.Decrypt(file.Ciphertext, file.Iv, file.Tag, masterKey);
            }
            catch (CryptographicException ex)
            {
                // AES-GCM tag mismatch is the wrong-password case. The exception message
                // is intentionally generic — the type tells the caller everything; no
                // detail leaks into logs.
                _logger.LogWarning("Login failed — invalid master password");
                throw new InvalidMasterPasswordException(ex);
            }

            _dekHolder.Set(dek);

            // Cache write failure does not fail the login — the user got in. Logged at
            // Warning because the next cold start will need the password again.
            try
            {
                await _keyVault
                    .SetCachedMasterKeyAsync(masterKey, _cacheTtl, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Login succeeded but DPAPI cache write failed");
            }

            _logger.LogInformation("Login completed via master password");
        }
        finally
        {
            if (masterKey is not null)
            {
                Array.Clear(masterKey, 0, masterKey.Length);
            }
            if (dek is not null)
            {
                Array.Clear(dek, 0, dek.Length);
            }
        }
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        // Order: clear holder first so a concurrent DB operation fails fast on an
        // unavailable DEK rather than running with a key whose cache was just wiped.
        try
        {
            _dekHolder.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout step failed: clear DEK holder");
        }

        try
        {
            await _keyVault.InvalidateMasterKeyCacheAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout step failed: invalidate DPAPI cache");
        }

        _logger.LogInformation("Logout completed");
    }

    private async Task SafeInvalidateCacheAsync()
    {
        try
        {
            await _keyVault.InvalidateMasterKeyCacheAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate stale DPAPI cache after cached-key login failure");
        }
    }
}
