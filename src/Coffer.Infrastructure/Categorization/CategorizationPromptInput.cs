using System.Text;
using Coffer.Infrastructure.AI;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Narrows a transaction description to what the categorisation model needs (Sprint 30): the
/// unlabelled lead (operation type or a plain merchant string), a counterparty that is a legal entity
/// (<c>ZUS</c>, <c>KAWIARNIA CENTRUM</c>, <c>ORANGE POLSKA S.A.</c>: for utilities, telcos, offices
/// and employers it is the main signal), the transfer title and the card/BLIK location (merchant
/// name and city). A person's name or pseudonym, address, phone, identifier, account, card number
/// and bookkeeping metadata are dropped: category quality should not depend on personal data. A
/// description with no recognised labels (a plain <c>BIEDRONKA 1234 TORUN</c>) is kept whole. Build
/// it from the redacted description: a title can carry a PESEL or a phone number.
/// </summary>
public static class CategorizationPromptInput
{
    public static string Build(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        var fields = StatementDescriptionFields.Parse(description);
        if (fields.Count == 0)
        {
            return description.Trim();
        }

        var sb = new StringBuilder(description.Length);
        Append(sb, description[..fields[0].LabelStart]);
        foreach (var field in fields)
        {
            if (field.ValueLength == 0)
            {
                continue;
            }

            if (field.Kind is DescriptionFieldKind.Title or DescriptionFieldKind.Location)
            {
                Append(sb, description[field.LabelStart..field.ValueEnd]);
            }
            else if (StatementDescriptionFields.CounterpartyNameOf(description, field) is { } name
                && CounterpartyNames.IsLegalEntity(name))
            {
                Append(sb, description[field.LabelStart..field.ValueStart] + name);
            }
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string part)
    {
        var trimmed = part.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(trimmed);
    }
}
