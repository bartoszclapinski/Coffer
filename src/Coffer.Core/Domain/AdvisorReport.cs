namespace Coffer.Core.Domain;

/// <summary>
/// The advisor's once-a-day AI commentary (doc 07): the LLM turns the engine's deterministic
/// verdicts into human-language per-goal risks and grounded cutting suggestions. The daily snapshot
/// job writes one report per day; the UI renders it without re-calling the LLM on every refresh.
/// <see cref="Date"/> is the day covered (transaction-scale <see cref="DateOnly"/>),
/// <see cref="GeneratedAt"/> is a UTC system timestamp (hard rule #2). <see cref="GeneratedByAi"/>
/// is <c>false</c> when generation fell back to engine-only output (provider error or budget gate).
/// </summary>
public class AdvisorReport
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    public DateTime GeneratedAt { get; set; }

    public bool GeneratedByAi { get; set; }

    public List<AdvisorSuggestion> Entries { get; set; } = [];
}
