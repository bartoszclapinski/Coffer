namespace Coffer.Core.Security;

public interface ISeedManager
{
    string GenerateMnemonic();

    bool IsValid(string mnemonic);

    Task<byte[]> DeriveRecoveryKeyAsync(
        string mnemonic,
        string passphrase,
        CancellationToken ct);
}
