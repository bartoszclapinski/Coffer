using Coffer.Shared.Parsing;

namespace Coffer.Core.Parsing;

/// <summary>
/// Bank-specific parser that turns a <see cref="StatementInput"/> into a
/// <see cref="ParseResult"/>. One implementation per supported (bank, format)
/// pair. A parser declares the single <see cref="StatementFormat"/> it consumes;
/// the registry resolves on (<see cref="BankCode"/>, <see cref="Format"/>).
/// </summary>
public interface IStatementParser
{
    /// <summary>Stable bank code matching <see cref="BankFingerprint.BankCode"/>.</summary>
    string BankCode { get; }

    /// <summary>The export format this parser consumes.</summary>
    StatementFormat Format { get; }

    /// <summary>
    /// Whether this parser handles the supplied fingerprint. Implementations
    /// typically return <c>fingerprint.BankCode == BankCode</c> but may return
    /// <c>true</c> for several codes (e.g. a future AI fallback that handles all
    /// unknown banks).
    /// </summary>
    bool CanHandle(BankFingerprint fingerprint);

    /// <summary>
    /// Parses the statement. Reads <see cref="StatementInput.Content"/> from
    /// position 0; does not dispose it. Throws a parser-specific exception for
    /// an unsupported layout (e.g. <c>UnsupportedCsvLayoutException</c> when the
    /// CSV header shape does not match).
    /// </summary>
    Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct);
}
