using System.Text;
using Coffer.Infrastructure.AI;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Narrows a transaction description to what the categorisation model needs (Sprint 30): the
/// unlabelled lead (operation type or a plain merchant string), the transfer title and the
/// card/BLIK location (merchant name and city). Counterparty name, address, phone, identifier,
/// account, card number and bookkeeping metadata are dropped: category quality should not depend
/// on personal data. A description with no recognised labels (a non-PKO bank, a plain
/// <c>BIEDRONKA 1234 TORUN</c>) is kept whole. The result is still passed through the redaction
/// session: a title can carry a PESEL or a phone number.
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
            if ((field.Kind is DescriptionFieldKind.Title or DescriptionFieldKind.Location) && field.ValueLength > 0)
            {
                Append(sb, description[field.LabelStart..field.ValueEnd]);
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
