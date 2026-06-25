namespace Coffer.Core.Goals;

/// <summary>
/// One spending category's current spend against its 6-month average (doc 07), the grounding the
/// advisor's cutting suggestions must cite. Money is <c>decimal</c> (hard rule #1). The report
/// generator picks the categories most over their average so each suggestion can name a category
/// and a concrete comparison rather than a vague "spend less".
/// </summary>
public sealed record CategorySpending(string Category, decimal Current, decimal Average6m)
{
    /// <summary>How far the current spend sits above (positive) or below the 6-month average.</summary>
    public decimal Delta => Current - Average6m;
}
