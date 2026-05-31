using Coffer.Core.Parsing;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// Resolves the right <see cref="IStatementParser"/> for a detected
/// <see cref="BankFingerprint"/>. Sprint 7 throws
/// <see cref="UnsupportedBankException"/> for unknown banks; Sprint 8 swaps
/// the throw for an <c>AiAssistedParser</c> lookup at this same boundary so
/// no callsite has to change.
/// </summary>
public sealed class StatementParserRegistry
{
    private readonly Dictionary<string, IStatementParser> _parsers;

    public StatementParserRegistry(IEnumerable<IStatementParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers.ToDictionary(p => p.BankCode, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the parser registered for <paramref name="fingerprint"/>.
    /// </summary>
    /// <exception cref="UnsupportedBankException">
    /// Thrown when the fingerprint is <c>null</c> (detector failed) or when no
    /// parser is registered for the detected bank code.
    /// </exception>
    public IStatementParser Resolve(BankFingerprint? fingerprint)
    {
        if (fingerprint is null)
        {
            throw new UnsupportedBankException("UNKNOWN");
        }

        if (_parsers.TryGetValue(fingerprint.BankCode, out var parser))
        {
            return parser;
        }

        throw new UnsupportedBankException(fingerprint.BankCode);
    }
}
