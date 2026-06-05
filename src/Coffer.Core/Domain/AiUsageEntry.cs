namespace Coffer.Core.Domain;

/// <summary>
/// One row per AI call (doc 04): which provider/model, what for, token counts, and the
/// estimated cost in USD (vendor) and PLN (the user's cap). The cost ledger appends
/// these and sums them per month.
/// </summary>
public class AiUsageEntry
{
    public Guid Id { get; set; }

    public DateTime At { get; set; }

    public string Provider { get; set; } = "";

    public string Model { get; set; } = "";

    public string Purpose { get; set; } = "";

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    public decimal EstimatedCostPln { get; set; }
}
