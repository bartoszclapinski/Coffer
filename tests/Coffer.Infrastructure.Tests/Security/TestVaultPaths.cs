using Coffer.Core.Security;

namespace Coffer.Infrastructure.Tests.Security;

/// <summary>
/// Test fixture that points <see cref="IVaultPaths"/> at a per-test temp directory.
/// Implements <see cref="IDisposable"/> so the directory and any vault artefacts
/// inside it are torn down after the test, regardless of whether the test passes
/// or throws. Sprint-6 chore #45 — replaces the static <c>CofferPaths</c> that
/// previously blocked integration tests from running against anything other than
/// the developer's real <c>%LocalAppData%\Coffer\</c>.
/// </summary>
public sealed class TestVaultPaths : IVaultPaths, IDisposable
{
    public TestVaultPaths()
    {
        LocalAppDataFolder = Path.Combine(
            Path.GetTempPath(),
            "Coffer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(LocalAppDataFolder);
    }

    public string LocalAppDataFolder { get; }

    public string EncryptedDekFilePath =>
        Path.Combine(LocalAppDataFolder, "dek.encrypted");

    public string DatabaseFile =>
        Path.Combine(LocalAppDataFolder, "coffer.db");

    public string MasterKeyCacheFile =>
        Path.Combine(LocalAppDataFolder, "master-key.dpapi.cache");

    public string SecretsFolder =>
        Path.Combine(LocalAppDataFolder, "secrets");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(LocalAppDataFolder))
            {
                Directory.Delete(LocalAppDataFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. A locked file (SQLite pool, etc.) is not worth
            // failing the test for; the OS temp directory will be reaped eventually.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale.
        }
    }
}
