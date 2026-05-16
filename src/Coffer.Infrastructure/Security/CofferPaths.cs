namespace Coffer.Infrastructure.Security;

public static class CofferPaths
{
    public static string LocalAppDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Coffer");

    public static string MasterKeyCacheFile() =>
        Path.Combine(LocalAppDataFolder(), "master-key.dpapi.cache");

    public static string EncryptedDekFilePath() =>
        Path.Combine(LocalAppDataFolder(), "dek.encrypted");

    public static string DatabaseFile() =>
        Path.Combine(LocalAppDataFolder(), "coffer.db");
}
