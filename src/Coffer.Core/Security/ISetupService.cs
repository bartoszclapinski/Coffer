namespace Coffer.Core.Security;

/// <summary>
/// Orchestrates the first-run setup: derives the master key, generates a random DEK,
/// writes the encrypted DEK file as the success sentinel, caches the master key,
/// publishes the DEK to <see cref="IDekHolder"/> and runs the initial database
/// migration. Implementations must follow an atomic-success / full-rollback
/// pattern — a partial failure must not leave <c>dek.encrypted</c> on disk pointing
/// to a non-existent vault.
/// </summary>
public interface ISetupService
{
    Task CompleteSetupAsync(string masterPassword, string mnemonic, CancellationToken ct);
}
