namespace Coffer.Core.Parsing;

/// <summary>
/// Result of <see cref="IBankDetector.Detect"/>. The detector reads the first page
/// of the PDF, matches one of a small set of identifying phrases, and returns the
/// fingerprint with the highest <see cref="Priority"/>.
/// </summary>
/// <param name="BankCode">Stable code used as the registry lookup key (e.g. "PKO_BP", "ING").</param>
/// <param name="BankName">The exact Polish bank name as it appears in statements; used for matching.</param>
/// <param name="Priority">
/// When multiple fingerprints match the same document (rare — usually only on white-label / cobranded statements),
/// the higher-priority entry wins.
/// </param>
public sealed record BankFingerprint(string BankCode, string BankName, int Priority);
