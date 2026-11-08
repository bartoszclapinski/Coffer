using Coffer.Core.Backup;
using Coffer.Core.Security;
using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.DependencyInjection;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class BackupRegistrationTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();

    public void Dispose() => _vaultPaths.Dispose();

    [Fact]
    public void AddCofferBackup_ResolvesEveryBackupServiceWithTheDekHolder()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .AddSingleton<IVaultPaths>(_vaultPaths)
            .AddSingleton<IDekHolder, DekHolder>()
            .AddCofferBackup()
            .AddSingleton<IPreMigrationBackup, PreMigrationBackup>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IBackupService>().Should().BeOfType<BackupService>();
        provider.GetRequiredService<IArchiveExporter>().Should().NotBeNull();
        provider.GetRequiredService<IRestoreService>().Should().NotBeNull();
        provider.GetRequiredService<IPreMigrationBackup>().Should().NotBeNull();
    }
}
