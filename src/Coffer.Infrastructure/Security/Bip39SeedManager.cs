using Coffer.Core.Security;
using NBitcoin;

namespace Coffer.Infrastructure.Security;

public sealed class Bip39SeedManager : ISeedManager
{
    private const int _recoveryKeyBytes = 32;

    public string GenerateMnemonic() =>
        new Mnemonic(Wordlist.English, WordCount.Twelve).ToString();

    public bool IsValid(string mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            return false;
        }

        try
        {
            var bip39 = new Mnemonic(mnemonic, Wordlist.English);
            // NBitcoin's constructor accepts an unknown-checksum mnemonic; verify explicitly.
            return bip39.IsValidChecksum;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            // Word count, word membership, or other structural failure.
            return false;
        }
    }

    public Task<byte[]> DeriveRecoveryKeyAsync(
        string mnemonic,
        string passphrase,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        ArgumentNullException.ThrowIfNull(passphrase);

        return Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();

                var bip39 = new Mnemonic(mnemonic, Wordlist.English);
                var seed = bip39.DeriveSeed(passphrase);
                try
                {
                    var recoveryKey = new byte[_recoveryKeyBytes];
                    Buffer.BlockCopy(seed, 0, recoveryKey, 0, _recoveryKeyBytes);
                    return recoveryKey;
                }
                finally
                {
                    Array.Clear(seed, 0, seed.Length);
                }
            },
            ct);
    }
}
