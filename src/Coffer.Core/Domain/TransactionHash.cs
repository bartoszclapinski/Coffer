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
/// same-amount pair.
/// <para>
/// Two genuinely identical transactions in one statement (e.g. two coffees at the
/// same café for the same amount on the same day) share that key. Since Sprint 30
/// the import passes a within-file <c>occurrence</c> ordinal: the first occurrence
/// hashes exactly as before (so rows imported by earlier versions still dedup), and
/// the n-th (n ≥ 2) appends <c>|#n</c> to the payload. Re-importing the same file
/// reproduces the same ordinals, so cross-import dedup is unchanged.
/// </para>
/// </remarks>
public static class TransactionHash
{
    public static string Compute(
        string accountNumber,
        DateOnly date,
        decimal amount,
        string normalizedDescription) =>
        Compute(accountNumber, date, amount, normalizedDescription, occurrence: 1);

    /// <summary>
    /// Hash for the <paramref name="occurrence"/>-th row (1-based) with this
    /// (account, date, amount, description) key within one statement file.
    /// <c>occurrence: 1</c> is identical to the four-argument overload.
    /// </summary>
    public static string Compute(
        string accountNumber,
        DateOnly date,
        decimal amount,
        string normalizedDescription,
        int occurrence)
    {
        ArgumentNullException.ThrowIfNull(accountNumber);
        ArgumentNullException.ThrowIfNull(normalizedDescription);
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrence, 1);

        var payload = string.Create(CultureInfo.InvariantCulture,
            $"{accountNumber}|{date:yyyy-MM-dd}|{amount.ToString("F2", CultureInfo.InvariantCulture)}|{normalizedDescription}");
        if (occurrence > 1)
        {
            payload = string.Create(CultureInfo.InvariantCulture, $"{payload}|#{occurrence}");
        }

        Span<byte> input = stackalloc byte[Encoding.UTF8.GetMaxByteCount(payload.Length)];
        var written = Encoding.UTF8.GetBytes(payload, input);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input[..written], hash);

        return Convert.ToHexString(hash);
    }
}
