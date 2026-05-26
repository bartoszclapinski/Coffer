using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

/// <summary>
/// Sprint-6 chore #45 unblocked the happy-path integration tests by making
/// <see cref="Coffer.Core.Security.IVaultPaths"/> injectable. The rollback path
/// stays here too — it was the only test that survived the static-paths era.
/// </summary>
public class SetupServiceTests
{
    private const string _validMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public async Task CompleteSetupAsync_WithValidCredentials_WritesDekFileAndPublishesDek()
    {
        using var paths = new TestVaultPaths();
        var dekHolder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        var keyDerivation = new Argon2KeyDerivation();
        var factory = new TempDbContextFactory(paths.DatabaseFile, dekHolder);

        var setupService = new SetupService(
            keyDerivation,
            keyVault,
            dekHolder,
            paths,
            () => factory,
            NullLogger<SetupService>.Instance);

        await setupService.CompleteSetupAsync(
            "StrongTestPassword123!",
            _validMnemonic,
            CancellationToken.None);

        File.Exists(paths.EncryptedDekFilePath).Should().BeTrue(
            "dek.encrypted is the on-disk success sentinel; it must be present after a clean setup");
        File.Exists(paths.DatabaseFile).Should().BeTrue(
            "coffer.db is created by the InitialCreate migration during setup");
        dekHolder.IsAvailable.Should().BeTrue(
            "the DEK must be published to the holder so CofferDbContext can open the encrypted DB");
        var cachedKey = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        cachedKey.Should().NotBeNull(
            "successful setup must refresh the DPAPI cache with the derived master key");

        // Pooling=False on the connection string keeps SQLite from caching handles,
        // and TestVaultPaths.Dispose absorbs any best-effort IOException from a
        // straggling lock — no explicit pool-flush needed.
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenDbContextFactoryThrows_ClearsDekHolderAndKeyVault()
    {
        using var paths = new TestVaultPaths();
        var dekHolder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        var keyDerivation = new Argon2KeyDerivation();
        var failingFactory = new FailingDbContextFactory();

        var setupService = new SetupService(
            keyDerivation,
            keyVault,
            dekHolder,
            paths,
            () => failingFactory,
            NullLogger<SetupService>.Instance);

        var act = async () => await setupService.CompleteSetupAsync(
            "StrongTestPassword123!",
            _validMnemonic,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        dekHolder.IsAvailable.Should().BeFalse(
            "rollback must clear the DEK holder when migration setup fails");
        var cached = await keyVault.GetCachedMasterKeyAsync(CancellationToken.None);
        cached.Should().BeNull(
            "no master-key cache should remain when the setup never reached the cache-write step");
        File.Exists(paths.EncryptedDekFilePath).Should().BeFalse(
            "dek.encrypted must not survive a failed setup");
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenVaultFileAlreadyExists_ThrowsVaultAlreadyExists()
    {
        using var paths = new TestVaultPaths();
        await File.WriteAllBytesAsync(paths.EncryptedDekFilePath, new byte[] { 0x01 });

        var setupService = new SetupService(
            new Argon2KeyDerivation(),
            new InMemoryKeyVault(),
            new DekHolder(),
            paths,
            () => new FailingDbContextFactory(),
            NullLogger<SetupService>.Instance);

        var act = async () => await setupService.CompleteSetupAsync(
            "StrongTestPassword123!",
            _validMnemonic,
            CancellationToken.None);

        await act.Should().ThrowAsync<Coffer.Core.Security.VaultAlreadyExistsException>();
    }

    private sealed class FailingDbContextFactory : IDbContextFactory<CofferDbContext>
    {
        public CofferDbContext CreateDbContext() =>
            throw new InvalidOperationException("test factory always fails");

        public Task<CofferDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<CofferDbContext>(
                new InvalidOperationException("test factory always fails"));
    }

    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> wired to a fixed temp path
    /// and a <see cref="DekHolder"/>. <see cref="SetupService"/> publishes the DEK
    /// to the holder before the first <see cref="CreateDbContextAsync"/> call, so
    /// the interceptor sees the right key on connection-open.
    /// </summary>
    private sealed class TempDbContextFactory : IDbContextFactory<CofferDbContext>
    {
        private readonly string _dbPath;
        private readonly DekHolder _dekHolder;

        public TempDbContextFactory(string dbPath, DekHolder dekHolder)
        {
            _dbPath = dbPath;
            _dekHolder = dekHolder;
        }

        public CofferDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<CofferDbContext>()
                .UseSqlite($"Data Source={_dbPath};Pooling=False;")
                .AddInterceptors(new SqlCipherKeyInterceptor(_dekHolder.Get()))
                .Options;
            return new CofferDbContext(options);
        }

        public Task<CofferDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
