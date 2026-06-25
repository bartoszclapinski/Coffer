using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// Read-side query for the Doradca page's advisor commentary: the most recent <see cref="AdvisorReport"/>
/// (written once a day by the snapshot job) with its <see cref="AdvisorReport.Entries"/> loaded, so the
/// page can render the AI risks and cutting suggestions without re-calling the LLM on every refresh.
/// </summary>
public interface IAdvisorReportQuery
{
    Task<AdvisorReport?> GetLatestAsync(CancellationToken ct);
}
