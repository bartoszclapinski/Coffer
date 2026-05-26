namespace Coffer.Core.Security;

/// <summary>
/// The on-disk locations of every Coffer vault artefact. Injecting this contract
/// (rather than reaching into a static <c>CofferPaths</c>) lets integration tests
/// point services at a temp directory instead of the developer's
/// <c>%LocalAppData%\Coffer\</c>, where any happy-path test would otherwise
/// destroy the real vault.
/// </summary>
public interface IVaultPaths
{
    /// <summary>The folder that contains every other path returned by this contract.</summary>
    string LocalAppDataFolder { get; }

    /// <summary>The encrypted DEK file (also the on-disk success sentinel after setup).</summary>
    string EncryptedDekFilePath { get; }

    /// <summary>The SQLCipher-encrypted SQLite database.</summary>
    string DatabaseFile { get; }

    /// <summary>The DPAPI master-key cache (7-day TTL on Windows).</summary>
    string MasterKeyCacheFile { get; }
}
