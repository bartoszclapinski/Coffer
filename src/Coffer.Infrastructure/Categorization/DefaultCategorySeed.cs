using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Seeds the opinionated default Polish category set and a starter rule pack on first
/// run (<see cref="ICategorySeed"/>). Categories are written only when the table is empty,
/// rules only when theirs is — so the owner's edits are never overwritten. Rule patterns
/// match the upper-cased, diacritic-preserving <c>NormalizedDescription</c>.
/// </summary>
public sealed class DefaultCategorySeed : ICategorySeed
{
    // Display name → colour chip. Order is irrelevant; the picker sorts by name.
    private static readonly (string Name, string Color)[] _categories =
    [
        ("Spożywcze", "#34C759"),
        ("Paliwo", "#FF9500"),
        ("Restauracje", "#FF2D55"),
        ("Subskrypcje", "#5856D6"),
        ("Edukacja", "#00C7BE"),
        ("Rozrywka", "#AF52DE"),
        ("Zdrowie", "#FF3B30"),
        ("Transport", "#007AFF"),
        ("Mieszkanie", "#8E8E93"),
        ("Ubrania", "#FF6482"),
        ("Kredyt hipoteczny", "#A2845E"),
        ("Inwestycje", "#30B0C7"),
        ("Wpływy", "#32D74B"),
        ("Inne", "#636366"),
    ];

    // Lower Priority wins; more specific merchants come first. Patterns are regex over the
    // normalised (upper-case) description, evaluated case-insensitively.
    private static readonly (string CategoryName, int Priority, string Pattern)[] _rules =
    [
        ("Subskrypcje", 10, @"NETFLIX|SPOTIFY|ANTHROPIC|OPENAI|HBO|DISNEY|YOUTUBE"),
        ("Spożywcze", 20, @"LIDL|BIEDRONKA|ŻABKA|ZABKA|KAUFLAND|AUCHAN|CARREFOUR|\bDINO\b"),
        ("Paliwo", 30, @"ORLEN|SHELL|\bBP\b|CIRCLE\s?K|MOYA|AMIC|LOTOS"),
        ("Restauracje", 40, @"MCDONALD|\bKFC\b|PIZZA|RESTAURACJA|BURGER|GLOVO|PYSZNE|UBER\s?EATS"),
        ("Zdrowie", 50, @"APTEKA|PRZYCHODNIA|MEDICOVER|LUX\s?MED"),
        ("Transport", 60, @"\bUBER\b|\bBOLT\b|\bMPK\b|\bPKP\b|JAKDOJADE|KOLEJE"),
        ("Kredyt hipoteczny", 70, @"HIPOTE"),
        ("Wpływy", 80, @"WYNAGRODZENIE|PENSJA|WPŁYW|WPLYW"),
    ];

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly ILogger<DefaultCategorySeed> _logger;

    public DefaultCategorySeed(
        IDbContextFactory<CofferDbContext> contextFactory, ILogger<DefaultCategorySeed> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var inserted = 0;

        if (!await db.Categories.AnyAsync(ct).ConfigureAwait(false))
        {
            foreach (var (name, color) in _categories)
            {
                db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = name, Color = color });
                inserted++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (!await db.Rules.AnyAsync(ct).ConfigureAwait(false))
        {
            var categoriesByName = await db.Categories
                .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            foreach (var (categoryName, priority, pattern) in _rules)
            {
                if (!categoriesByName.TryGetValue(categoryName, out var categoryId))
                {
                    // The matching category was archived or renamed by the user — skip the
                    // starter rule rather than recreate a category they removed.
                    continue;
                }

                db.Rules.Add(new Rule
                {
                    Id = Guid.NewGuid(),
                    Priority = priority,
                    Pattern = pattern,
                    CategoryId = categoryId,
                    IsEnabled = true,
                });
                inserted++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (inserted > 0)
        {
            _logger.LogInformation("Seeded {Count} default category/rule rows", inserted);
        }

        return inserted;
    }
}
