using System.Text.Json;
using Coffer.Core.Anomalies;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Read-only chat tool that returns the active anomaly alerts (Phase 8) whose period overlaps a
/// date range, newest first. Dismissed alerts are excluded — they are suppressed for good. The
/// titles/descriptions are whatever was persisted, i.e. the 13-B LLM commentary for the top
/// findings and the templated text for the rest. Realises the <c>FindAnomalies</c> tool deferred
/// in Sprint 12.
/// </summary>
public sealed class FindAnomaliesTool : ChatTool
{
    public FindAnomaliesTool(IDbContextFactory<CofferDbContext> contextFactory)
        : base(contextFactory)
    {
    }

    public override string Name => "FindAnomalies";

    public override string Description =>
        "Lista wykrytych anomalii (alertów) w wydatkach, których okres pokrywa się z podanym "
        + "przedziałem dat — np. duplikaty płatności, wysokie kwoty, nowe sklepy, brakujące "
        + "subskrypcje. Od najnowszych. Pomija odrzucone alerty. Daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "Data początkowa włącznie, format RRRR-MM-DD." },
            "to": { "type": "string", "description": "Data końcowa włącznie, format RRRR-MM-DD." }
          },
          "required": ["from", "to"]
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        if (!TryGetDate(args, "from", out var from) || !TryGetDate(args, "to", out var to))
        {
            return ErrorObject("Both 'from' and 'to' are required dates in RRRR-MM-DD format.");
        }

        if (from > to)
        {
            return ErrorObject("'from' must be on or before 'to'.");
        }

        var rows = await db.Alerts.AsNoTracking()
            .Where(a => a.Status != AlertStatus.Dismissed && a.PeriodFrom <= to && a.PeriodTo >= from)
            .OrderByDescending(a => a.DetectedAt)
            .Select(a => new
            {
                a.Type,
                a.Title,
                a.Description,
                a.RelatedAmount,
                a.PeriodFrom,
                a.PeriodTo,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var anomalies = rows
            .Select(r => new
            {
                type = r.Type.ToString(),
                title = r.Title,
                description = r.Description,
                amount = r.RelatedAmount,
                periodFrom = Iso(r.PeriodFrom),
                periodTo = Iso(r.PeriodTo),
            })
            .ToList();

        return new { from = Iso(from), to = Iso(to), count = anomalies.Count, anomalies };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
