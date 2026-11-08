namespace Coffer.Core.Ai;

/// <summary>
/// Redaction state for one AI request, opened through <see cref="IPromptAnonymizer"/>. Removes
/// account numbers, IBANs, NIPs, PESELs, phone numbers, the values of personal labelled statement
/// sub-fields (counterparty name, address, phone, identifier, account), street addresses and postal
/// codes in free text, and the owner's identity names. Counterparty names of persons become stable
/// per-session pseudonyms (e.g. <c>[PARTY_1]</c>) so the model can still tell two payees apart;
/// <see cref="Restore"/> maps them back in model output that is shown to the owner. Merchant-like
/// content (legal-entity counterparties such as <c>ZUS</c>, the transfer title, a card location such
/// as <c>BIEDRONKA 1234</c>), amounts and dates are kept: they carry the signal and are not personal.
/// </summary>
public interface IPromptRedactionSession
{
    /// <summary>
    /// Whether at least one owner identity name is configured. When <c>false</c> the owner's own name
    /// (e.g. on a statement header) cannot be redacted, and callers that send whole documents should
    /// warn the owner.
    /// </summary>
    bool HasOwnerNames { get; }

    /// <summary>
    /// Returns <paramref name="text"/> with every personal datum replaced by a placeholder. For
    /// transaction descriptions and prompts built from them (fields already unescaped).
    /// </summary>
    string Redact(string text);

    /// <summary>
    /// <see cref="Redact"/> for a raw statement document (CSV or PDF text) sent whole, as the AI
    /// fallback parser does: labelled values also end at CSV field boundaries (closing quote,
    /// delimiter), so the document keeps its structure.
    /// </summary>
    string RedactDocument(string text);

    /// <summary>
    /// Replaces the counterparty pseudonyms this session issued with the original names. Use it only
    /// on model output that stays on the device; restored text must be redacted again before it goes
    /// back into any prompt.
    /// </summary>
    string Restore(string text);
}
