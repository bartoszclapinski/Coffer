using UglyToad.PdfPig;

namespace Coffer.Core.Parsing;

/// <summary>
/// Inspects a <see cref="PdfDocument"/> and returns the matching
/// <see cref="BankFingerprint"/> (or <c>null</c> when no fingerprint matches —
/// e.g. an unknown bank or a scanned PDF without extractable text). Sprint 7
/// ships <c>FingerprintBankDetector</c> which performs a case-insensitive
/// substring match against the first page's text.
/// </summary>
/// <remarks>
/// PdfPig is a read-only PDF parsing library, not a UI framework. The Core
/// project takes a runtime dependency on it (carve-out from hard rule #3
/// resolved in Sprint 7 plan, open question #1) so that the contract operates
/// on the same document model every implementation uses, without an extra
/// abstraction layer that would have to leak every <see cref="UglyToad.PdfPig.Content.Page"/>
/// member anyway.
/// </remarks>
public interface IBankDetector
{
    BankFingerprint? Detect(PdfDocument document);
}
