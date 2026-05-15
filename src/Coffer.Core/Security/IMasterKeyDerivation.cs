namespace Coffer.Core.Security;

public interface IMasterKeyDerivation
{
    Task<byte[]> DeriveMasterKeyAsync(
        string password,
        byte[] salt,
        Argon2Parameters parameters,
        CancellationToken ct);
}
