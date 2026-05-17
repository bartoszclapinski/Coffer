using System.Security.Cryptography;
using Coffer.Core.Security;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Orchestrates the first-run setup in an atomic-success / full-rollback pattern.
/// The encrypted DEK file is written last and becomes the on-disk sentinel only on
/// success — a partial failure rolls back every side-effect (DEK file, key vault
/// cache, in-memory holder, database file) so the user can simply retry.
/// </summary>
public sealed class SetupService : ISetupService
{
    private readonly IMasterKeyDerivation _keyDerivation;
    private readonly IKeyVault _keyVault;
    private readonly IDekHolder _dekHolder;
    private readonly Func<IDbContextFactory<CofferDbContext>> _getDbContextFactory;
    private readonly ILogger<SetupService> _logger;

    /// <summary>
    /// The <see cref="IDbContextFactory{TContext}"/> is resolved through a delegate to
    /// avoid eager construction of <c>DbContextOptions</c> at SetupService creation
    /// time — that path runs the registered <c>dekProvider</c>, which throws on
    /// <see cref="IDekHolder.Get"/> before the wizard has had a chance to publish
    /// the DEK. The delegate is invoked once, after <see cref="IDekHolder.Set"/> in
    /// <see cref="CompleteSetupAsync"/>.
    /// </summary>
    public SetupService(
        IMasterKeyDerivation keyDerivation,
        IKeyVault keyVault,
        IDekHolder dekHolder,
        Func<IDbContextFactory<CofferDbContext>> getDbContextFactory,
        ILogger<SetupService> logger)
    {
        ArgumentNullException.ThrowIfNull(keyDerivation);
        ArgumentNullException.ThrowIfNull(keyVault);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(getDbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _keyDerivation = keyDerivation;
        _keyVault = keyVault;
        _dekHolder = dekHolder;
        _getDbContextFactory = getDbContextFactory;
        _logger = logger;
    }

    public async Task CompleteSetupAsync(string masterPassword, string mnemonic, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        ArgumentNullException.ThrowIfNull(mnemonic);

        var parameters = Argon2Parameters.Default;
        var salt = RandomNumberGenerator.GetBytes(parameters.SaltBytes);
        var masterKey = await _keyDerivation
            .DeriveMasterKeyAsync(masterPassword, salt, parameters, ct)
            .ConfigureAwait(false);

        var dek = RandomNumberGenerator.GetBytes(32);

        var dekHolderWasSet = false;
        var databaseWasCreated = false;
        var dekFileWasWritten = false;
        var keyVaultCacheWasSet = false;

        try
        {
            _dekHolder.Set(dek);
            dekHolderWasSet = true;

            var dbContextFactory = _getDbContextFactory();
            await using (var db = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                databaseWasCreated = true;

                var runner = new MigrationRunner(
                    db,
                    NullLoggerForSetup(),
                    preMigrationBackup: _ =>
                    {
                        // Hard rule #8: pre-migration-backup runs every time, no exceptions.
                        // On a fresh install there is nothing to back up; the no-op makes
                        // the literal contract honest and keeps the mechanism hot.
                        _logger.LogInformation("Fresh install — no data to back up");
                        return Task.CompletedTask;
                    });
                var result = await runner.RunPendingMigrationsAsync(ct).ConfigureAwait(false);
                if (result.Status != MigrationStatus.Migrated)
                {
                    throw new InvalidOperationException(
                        $"Initial migration did not produce Migrated status: {result.Status}");
                }
            }

            var encryptionResult = AesGcmCrypto.Encrypt(dek, masterKey);
            try
            {
                var file = new DekFile(
                    Version: DekFile.CurrentVersion,
                    ArgonParameters: parameters,
                    Salt: salt,
                    Iv: encryptionResult.Iv,
                    Tag: encryptionResult.Tag,
                    Ciphertext: encryptionResult.Ciphertext);

                await DekFile.WriteAsync(file, CofferPaths.EncryptedDekFilePath(), ct).ConfigureAwait(false);
                dekFileWasWritten = true;

                await _keyVault.SetCachedMasterKeyAsync(masterKey, TimeSpan.FromDays(7), ct).ConfigureAwait(false);
                keyVaultCacheWasSet = true;
            }
            finally
            {
                Array.Clear(encryptionResult.Ciphertext, 0, encryptionResult.Ciphertext.Length);
                Array.Clear(encryptionResult.Tag, 0, encryptionResult.Tag.Length);
                Array.Clear(encryptionResult.Iv, 0, encryptionResult.Iv.Length);
            }

            _logger.LogInformation("Setup completed successfully");
        }
        catch (OperationCanceledException)
        {
            // Cancellation: do not surface as Failed, do not run rollback — the caller
            // requested abort; partial side-effects mirror any other cancelled async op.
            Array.Clear(masterKey, 0, masterKey.Length);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup failed; rolling back any persisted state");

            if (keyVaultCacheWasSet)
            {
                await SafeRollbackAsync(
                    "invalidate key vault cache",
                    () => _keyVault.InvalidateMasterKeyCacheAsync(CancellationToken.None)).ConfigureAwait(false);
            }

            if (dekFileWasWritten)
            {
                SafeRollback(
                    "delete dek.encrypted",
                    () => TryDelete(CofferPaths.EncryptedDekFilePath()));
            }

            if (dekHolderWasSet)
            {
                SafeRollback("clear DEK holder", _dekHolder.Clear);
            }

            if (databaseWasCreated)
            {
                SafeRollback(
                    "delete coffer.db",
                    () => TryDelete(CofferPaths.DatabaseFile()));
            }

            Array.Clear(masterKey, 0, masterKey.Length);
            throw;
        }

        Array.Clear(masterKey, 0, masterKey.Length);
    }

    private void SafeRollback(string description, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rollback step failed: {Description}", description);
        }
    }

    private async Task SafeRollbackAsync(string description, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rollback step failed: {Description}", description);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private ILogger<MigrationRunner> NullLoggerForSetup() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance;
}
