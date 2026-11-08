using Coffer.Core.Ai;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Reads the counterparty names out of the stored transaction descriptions: every value of a
/// labelled name sub-field (PKO's <c>Nazwa nadawcy:</c> / <c>Nazwa odbiorcy:</c>, and other banks'
/// <c>Odbiorca:</c>, <c>Dane nadawcy:</c>, <c>Kontrahent:</c>, … without their address part). Taking
/// them from the labels, rather than from <c>Transaction.Merchant</c>, keeps shop names that came
/// from other sources (e.g. an AI-fallback import's <c>BIEDRONKA</c>) out of the list. The redactor
/// then drops the legal entities among them, so only persons' names are pseudonymised.
/// </summary>
public sealed class StoredCounterpartyNameSource : ICounterpartyNameSource
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public StoredCounterpartyNameSource(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyCollection<string>> GetKnownCounterpartyNamesAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // A cheap pre-filter on the label words (SQLite LIKE is case-insensitive for ASCII, and every
        // counterparty label contains one of these ASCII fragments); the labels are parsed below.
        var descriptions = await db.Transactions.AsNoTracking()
            .Where(t => EF.Functions.Like(t.Description, "%nazwa%")
                || EF.Functions.Like(t.Description, "%odbiorc%")
                || EF.Functions.Like(t.Description, "%nadawc%")
                || EF.Functions.Like(t.Description, "%kontrahent%")
                || EF.Functions.Like(t.Description, "%zleceniodawc%")
                || EF.Functions.Like(t.Description, "%beneficjent%"))
            .Select(t => t.Description)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var description in descriptions)
        {
            foreach (var field in StatementDescriptionFields.Parse(description))
            {
                if (StatementDescriptionFields.CounterpartyNameOf(description, field) is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }
}
