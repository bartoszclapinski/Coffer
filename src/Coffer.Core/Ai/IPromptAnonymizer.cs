namespace Coffer.Core.Ai;

/// <summary>
/// Redacts personally identifying account data from text before it leaves the process
/// for an AI provider (hard rule #7). Merchant names are kept — they carry the
/// categorisation signal and are not sensitive; account numbers, IBANs and NIPs are not.
/// </summary>
public interface IPromptAnonymizer
{
    string Anonymize(string text);

    /// <summary>
    /// Same as <see cref="Anonymize(string)"/> but first redacts the supplied owner-identity
    /// names (the account holder's name as printed on a statement header), which the standard
    /// account/IBAN/NIP rules do not cover. Used by the AI fallback parser, whose prompt is the
    /// whole statement. An empty <paramref name="ownerNames"/> list leaves the standard pipeline
    /// unchanged — the caller is responsible for warning that the header may be un-redacted. The
    /// default implementation skips name redaction; the production anonymiser overrides it.
    /// </summary>
    string Anonymize(string text, IReadOnlyList<string> ownerNames) => Anonymize(text);
}
