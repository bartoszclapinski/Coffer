using Coffer.Shared.Parsing;
using UglyToad.PdfPig;

namespace Coffer.Core.Parsing;

/// <summary>
/// Bank-specific parser that turns a PdfPig <see cref="PdfDocument"/> into a
/// <see cref="ParseResult"/>. One implementation per supported bank; the future
/// AI-assisted fallback in Sprint 8 also implements this contract.
/// </summary>
public interface IStatementParser
{
    /// <summary>Stable bank code matching <see cref="BankFingerprint.BankCode"/>.</summary>
    string BankCode { get; }

    /// <summary>
    /// Whether this parser handles the supplied fingerprint. Implementations
    /// typically return <c>fingerprint.BankCode == BankCode</c> but may return
    /// <c>true</c> for several codes (e.g. a future AI fallback that handles all
    /// unknown banks).
    /// </summary>
    bool CanHandle(BankFingerprint fingerprint);

    /// <summary>
    /// Parses the document. Throws bank-specific exceptions for unsupported
    /// layouts (e.g. PKO BP throws <c>UnsupportedPkoLayoutException</c> for
    /// credit-card / savings / foreign-currency statements until Sprint 8 adds
    /// support).
    /// </summary>
    Task<ParseResult> ParseAsync(PdfDocument document, CancellationToken ct);
}
