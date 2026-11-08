using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Production implementation of <see cref="IVaultPaths"/>. Every path lives under the data root chosen by
/// <see cref="CofferDataRoot"/> — <c>COFFER_DATA_DIR</c> if set, else <c>%LocalAppData%\Coffer.Dev</c> for a
/// Debug build and <c>%LocalAppData%\Coffer</c> for a Release build. Registered as a singleton in
/// <c>AddCofferInfrastructure</c>.
/// </summary>
public sealed class CofferVaultPaths : IVaultPaths
{
    /// <summary>Paths under <see cref="CofferDataRoot.Current"/>.</summary>
    public CofferVaultPaths()
        : this(CofferDataRoot.Current)
    {
    }

    /// <summary>Paths under an explicit <paramref name="dataRoot"/>.</summary>
    public CofferVaultPaths(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        LocalAppDataFolder = dataRoot;
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
}
