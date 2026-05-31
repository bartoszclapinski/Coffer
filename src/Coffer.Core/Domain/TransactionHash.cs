using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coffer.Core.Domain;

/// <summary>
/// Deterministic SHA-256 hash that uniquely identifies a transaction for
/// dedup. Inputs are normalised so re-imports of the same statement (potentially
/// with benign printer whitespace / case differences) collapse to the same hash.
/// </summary>
/// <remarks>
/// Composition: <c>SHA256(accountNumber | yyyy-MM-dd | amount.F2 | normalizedDescription)</c>.
/// The account number scopes the hash to one account; date and amount carry most
/// of the uniqueness signal; normalized description disambiguates the rare same-day
/// same-amount pair. Edge case acknowledged in Sprint 7 plan, open question #3:
/// two genuinely-identical transactions in one statement (e.g. two BLIK
/// payments to the same merchant for the same amount on the same day) hash
/// identically and get deduped — acceptable for v1.
/// </remarks>
public static class TransactionHash
{
    public static string Compute(
        string accountNumber,
        DateOnly date,
        decimal amount,
        string normalizedDescription)
    {
        ArgumentNullException.ThrowIfNull(accountNumber);
        ArgumentNullException.ThrowIfNull(normalizedDescription);

        var payload = string.Create(CultureInfo.InvariantCulture,
            $"{accountNumber}|{date:yyyy-MM-dd}|{amount.ToString("F2", CultureInfo.InvariantCulture)}|{normalizedDescription}");

        Span<byte> input = stackalloc byte[Encoding.UTF8.GetMaxByteCount(payload.Length)];
        var written = Encoding.UTF8.GetBytes(payload, input);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input[..written], hash);

        return Convert.ToHexString(hash);
    }
}
