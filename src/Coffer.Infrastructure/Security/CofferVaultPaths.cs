using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Production implementation of <see cref="IVaultPaths"/> — points at
/// <c>%LocalAppData%\Coffer\</c> on Windows and the platform equivalent of
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/> elsewhere.
/// Registered as a singleton in <c>AddCofferInfrastructure</c>.
/// </summary>
public sealed class CofferVaultPaths : IVaultPaths
{
    public string LocalAppDataFolder { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Coffer");

    public string EncryptedDekFilePath =>
        Path.Combine(LocalAppDataFolder, "dek.encrypted");

    public string DatabaseFile =>
        Path.Combine(LocalAppDataFolder, "coffer.db");

    public string MasterKeyCacheFile =>
        Path.Combine(LocalAppDataFolder, "master-key.dpapi.cache");

    public string SecretsFolder =>
        Path.Combine(LocalAppDataFolder, "secrets");
}
