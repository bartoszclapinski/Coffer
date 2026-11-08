using Coffer.Core.Ai;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Reads the counterparty names out of the stored transaction descriptions: every value of a
/// labelled name sub-field (<c>Nazwa nadawcy:</c>, <c>Nazwa odbiorcy:</c>, …). Taking them from the
/// labels, rather than from <c>Transaction.Merchant</c>, keeps shop names that came from other
/// sources (e.g. an AI-fallback import's <c>BIEDRONKA</c>) out of the list, so they are not
/// pseudonymised in prompts where they are the useful signal.
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

        // SQLite LIKE is case-insensitive for ASCII, which is all "nazwa" needs.
        var descriptions = await db.Transactions.AsNoTracking()
            .Where(t => EF.Functions.Like(t.Description, "%nazwa%"))
            .Select(t => t.Description)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var description in descriptions)
        {
            foreach (var field in StatementDescriptionFields.Parse(description))
            {
                if (field.Kind == DescriptionFieldKind.CounterpartyName && field.ValueLength > 0)
                {
                    names.Add(description.Substring(field.ValueStart, field.ValueLength));
                }
            }
        }

        return names;
    }
}
