using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Loads the most recent <see cref="AdvisorReport"/> (by <see cref="AdvisorReport.Date"/>) with its
/// <see cref="AdvisorReport.Entries"/>, untracked, for the Doradca page. The daily snapshot job is the
/// only writer, so the latest report is the one to show.
/// </summary>
public sealed class AdvisorReportQuery : IAdvisorReportQuery
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public AdvisorReportQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<AdvisorReport?> GetLatestAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.AdvisorReports.AsNoTracking()
            .Include(r => r.Entries)
            .OrderByDescending(r => r.Date)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
