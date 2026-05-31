using Coffer.Shared.Parsing;

namespace Coffer.Core.Parsing;

/// <summary>
/// Inspects a <see cref="StatementInput"/> and returns the matching
/// <see cref="BankFingerprint"/> (or <c>null</c> when no fingerprint matches —
/// e.g. an unknown bank, a scanned PDF without extractable text, or a CSV whose
/// header signature is unrecognised). <c>FingerprintBankDetector</c> switches on
/// <see cref="StatementInput.Format"/>: for PDFs it matches a case-insensitive
/// substring against the first page's text; for CSVs it matches the export's
/// header signature.
/// </summary>
/// <remarks>
/// The detector operates on the format-neutral <see cref="StatementInput"/> so
/// that <c>Coffer.Core</c> keeps zero third-party runtime dependencies (hard
/// rule #3). PdfPig stays an Infrastructure-only dependency: the implementation
/// opens the <see cref="StatementInput.Content"/> stream with PdfPig when the
/// format is <see cref="StatementFormat.Pdf"/>.
/// </remarks>
public interface IBankDetector
{
    BankFingerprint? Detect(StatementInput input);
}
