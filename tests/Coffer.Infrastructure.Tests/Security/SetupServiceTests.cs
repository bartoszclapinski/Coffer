using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

/// <summary>
/// Rollback-focused unit tests. The happy path is exercised manually per the Sprint 5
/// manual-verification checklist; an in-process happy-path integration test would
/// require <see cref="CofferPaths"/> to be injectable (currently static, writes to
/// the user's <c>%LocalAppData%</c>). That refactor is a deferred follow-up.
/// </summary>
public class SetupServiceTests
{
    private const string _validMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public async Task CompleteSetupAsync_WhenDbContextFactoryThrows_ClearsDekHolderAndKeyVault()
    {
        var dekHolder = new DekHolder();
        var keyVault = new InMemoryKeyVault();
        var keyDerivation = new Argon2KeyDerivation();
        var failingFactory = new FailingDbContextFactory();

        var setupService = new SetupService(
            keyDerivation,
            keyVault,
            dekHolder,
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
    }

    private sealed class FailingDbContextFactory : IDbContextFactory<CofferDbContext>
    {
        public CofferDbContext CreateDbContext() =>
            throw new InvalidOperationException("test factory always fails");

        public Task<CofferDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<CofferDbContext>(
                new InvalidOperationException("test factory always fails"));
    }
}
