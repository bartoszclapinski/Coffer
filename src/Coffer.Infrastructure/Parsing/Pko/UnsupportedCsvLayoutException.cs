namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Thrown by <see cref="PkoHistoriaCsvParser"/> when the CSV does not match the
/// expected PKO BP "Historia rachunku" shape (wrong header signature, missing
/// columns, or an unparseable mandatory field).
/// </summary>
/// <remarks>
/// The message carries a short structural hint only — never raw row content —
/// so a parse failure cannot leak account numbers, names, or amounts into logs
/// (hard rules #6 / #11).
/// </remarks>
public sealed class UnsupportedCsvLayoutException : Exception
{
    public UnsupportedCsvLayoutException(string hint)
        : base($"CSV does not match the PKO 'Historia rachunku' layout: {hint}.")
    {
    }
}
