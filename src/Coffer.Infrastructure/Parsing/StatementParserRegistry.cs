using Coffer.Core.Parsing;
using Coffer.Shared.Parsing;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// Resolves the right <see cref="IStatementParser"/> for a detected
/// <see cref="BankFingerprint"/> and the input's <see cref="StatementFormat"/>.
/// A bank can offer several formats (PDF, CSV); each gets its own parser, so the
/// lookup key is (<c>BankCode</c>, <c>Format</c>). When no deterministic parser
/// matches and an AI-assisted <paramref name="fallback"/> is configured, that
/// fallback is returned so an unknown bank still imports without a code change
/// (Sprint 17). When no fallback is configured, <see cref="UnsupportedBankException"/>
/// is thrown as before. The fallback is supplied explicitly (not via the parser
/// enumeration) so a deterministic parser always wins for a known bank+format and
/// "did we use AI?" stays unambiguous.
/// </summary>
public sealed class StatementParserRegistry
{
    private readonly Dictionary<(string BankCode, StatementFormat Format), IStatementParser> _parsers;
    private readonly IStatementParser? _fallback;

    public StatementParserRegistry(IEnumerable<IStatementParser> parsers, IStatementParser? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers.ToDictionary(p => (p.BankCode, p.Format));
        _fallback = fallback;
    }

    /// <summary>
    /// Returns the deterministic parser registered for <paramref name="fingerprint"/> in the
    /// given <paramref name="format"/>; failing that, the AI-assisted fallback when one is
    /// configured.
    /// </summary>
    /// <exception cref="UnsupportedBankException">
    /// Thrown when no deterministic parser matches and no fallback is configured — the
    /// fingerprint is <c>null</c> (detector failed) or no parser is registered for the detected
    /// bank code in this format. (When a fallback exists it owns the opt-in/key/budget gating and
    /// throws this itself when AI parsing is unavailable.)
    /// </exception>
    public IStatementParser Resolve(BankFingerprint? fingerprint, StatementFormat format)
    {
        if (fingerprint is not null && _parsers.TryGetValue((fingerprint.BankCode, format), out var parser))
        {
            return parser;
        }

        if (_fallback is not null)
        {
            return _fallback;
        }

        throw new UnsupportedBankException(fingerprint?.BankCode ?? "UNKNOWN");
    }
}
