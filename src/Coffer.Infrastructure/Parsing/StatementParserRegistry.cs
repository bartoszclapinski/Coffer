using Coffer.Core.Parsing;
using Coffer.Shared.Parsing;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// Resolves the right <see cref="IStatementParser"/> for a detected
/// <see cref="BankFingerprint"/> and the input's <see cref="StatementFormat"/>.
/// A bank can offer several formats (PDF, CSV); each gets its own parser, so the
/// lookup key is (<c>BankCode</c>, <c>Format</c>). Unknown bank/format throws
/// <see cref="UnsupportedBankException"/>; a later sprint swaps that throw for an
/// AI-assisted parser at this same boundary so no callsite has to change.
/// </summary>
public sealed class StatementParserRegistry
{
    private readonly Dictionary<(string BankCode, StatementFormat Format), IStatementParser> _parsers;

    public StatementParserRegistry(IEnumerable<IStatementParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers.ToDictionary(p => (p.BankCode, p.Format));
    }

    /// <summary>
    /// Returns the parser registered for <paramref name="fingerprint"/> in the
    /// given <paramref name="format"/>.
    /// </summary>
    /// <exception cref="UnsupportedBankException">
    /// Thrown when the fingerprint is <c>null</c> (detector failed) or when no
    /// parser is registered for the detected bank code in this format.
    /// </exception>
    public IStatementParser Resolve(BankFingerprint? fingerprint, StatementFormat format)
    {
        if (fingerprint is null)
        {
            throw new UnsupportedBankException("UNKNOWN");
        }

        if (_parsers.TryGetValue((fingerprint.BankCode, format), out var parser))
        {
            return parser;
        }

        throw new UnsupportedBankException(fingerprint.BankCode);
    }
}
