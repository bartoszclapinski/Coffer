namespace Coffer.Core.Domain;

/// <summary>
/// One line of an <see cref="AdvisorReport"/> (doc 07). <see cref="Kind"/> decides which fields
/// carry meaning: a <see cref="AdvisorEntryKind.Risk"/> ties to a <see cref="GoalId"/> and uses
/// only <see cref="Description"/>; a <see cref="AdvisorEntryKind.Suggestion"/> is a cutting tip with
/// a <see cref="Title"/>, an estimated <see cref="Savings"/> (PLN/month — <c>decimal</c>, hard rule
/// #1), and a <see cref="CategoryAffected"/> it must cite. The LLM never invents the numbers; they
/// trace back to the engine and 6-month category averages.
/// </summary>
public class AdvisorSuggestion
{
    public Guid Id { get; set; }

    public Guid ReportId { get; set; }

    public AdvisorEntryKind Kind { get; set; }

    /// <summary>Set for a <see cref="AdvisorEntryKind.Risk"/>; null for a global suggestion.</summary>
    public Guid? GoalId { get; set; }

    public string Title { get; set; } = "";

    public decimal Savings { get; set; }

    public string Description { get; set; } = "";

    public string? CategoryAffected { get; set; }
}
