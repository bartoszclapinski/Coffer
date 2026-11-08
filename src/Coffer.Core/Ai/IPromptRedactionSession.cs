namespace Coffer.Core.Ai;

/// <summary>
/// Redaction state for one AI request, opened through <see cref="IPromptAnonymizer"/>. Removes
/// account numbers, IBANs, NIPs, PESELs, phone numbers, the values of personal labelled statement
/// sub-fields (counterparty name, address, phone, identifier, account) and the owner's identity
/// names. Counterparty names become stable per-session pseudonyms (e.g. <c>[PARTY_1]</c>) so the
/// model can still tell two payees apart; <see cref="Restore"/> maps them back in model output that
/// is shown to the owner. Merchant-like content (transfer title, card location such as
/// <c>BIEDRONKA 1234</c>), amounts and dates are kept: they carry the signal and are not personal.
/// </summary>
public interface IPromptRedactionSession
{
    /// <summary>
    /// Whether at least one owner identity name is configured. When <c>false</c> the owner's own name
    /// (e.g. on a statement header) cannot be redacted, and callers that send whole documents should
    /// warn the owner.
    /// </summary>
    bool HasOwnerNames { get; }

    /// <summary>Returns <paramref name="text"/> with every personal datum replaced by a placeholder.</summary>
    string Redact(string text);

    /// <summary>
    /// Replaces the counterparty pseudonyms this session issued with the original names. Use it only
    /// on model output that stays on the device; restored text must be redacted again before it goes
    /// back into any prompt.
    /// </summary>
    string Restore(string text);
}
