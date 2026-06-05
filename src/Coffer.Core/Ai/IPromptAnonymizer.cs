namespace Coffer.Core.Ai;

/// <summary>
/// Redacts personally identifying account data from text before it leaves the process
/// for an AI provider (hard rule #7). Merchant names are kept — they carry the
/// categorisation signal and are not sensitive; account numbers, IBANs and NIPs are not.
/// </summary>
public interface IPromptAnonymizer
{
    string Anonymize(string text);
}
