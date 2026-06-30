using System.Globalization;
using System.Text.Json;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Base for the read-only chat tools. Owns a short-lived <c>AsNoTracking</c> context per call,
/// parses and validates the model's JSON arguments centrally, and scopes every query to the single
/// display currency (PLN, like the dashboard; multi-currency reasoning is a later phase). Subclasses
/// implement <see cref="RunAsync"/> with server-side aggregation only.
/// </summary>
public abstract class ChatTool : IChatTool
{
    /// <summary>Single display currency for v1 (doc 04 / dashboard parity).</summary>
    private protected const string DisplayCurrency = "PLN";

    /// <summary>Label for transactions with no category (matches the dashboard).</summary>
    private protected const string UncategorizedLabel = "Bez kategorii";

    private protected static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    protected ChatTool(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string ParametersJsonSchema { get; }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Serialize(ErrorObject("Arguments were not valid JSON."));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var result = await RunAsync(args, db, ct).ConfigureAwait(false);
        return Serialize(result);
    }

    private protected abstract Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct);

    private protected static string Serialize(object value) => JsonSerializer.Serialize(value, _jsonOptions);

    private protected static object ErrorObject(string message) => new { error = message };

    private protected static bool TryGetDate(JsonElement args, string name, out DateOnly value)
    {
        value = default;
        return args.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private protected static string? GetString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private protected static int GetInt(JsonElement args, string name, int fallback) =>
        args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : fallback;

    /// <summary>
    /// Resolves a Polish category name to a query filter. No name → no category predicate; the
    /// uncategorised label → <c>CategoryId == null</c>; a real category (case-insensitive exact
    /// match) → that id; an unknown name → <see cref="CategoryMatchKind.Unknown"/> so the tool can
    /// short-circuit to an empty result rather than erroring (doc 04).
    /// </summary>
    private protected static async Task<CategoryMatch> ResolveCategoryAsync(
        CofferDbContext db, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new CategoryMatch(CategoryMatchKind.NoFilter, null);
        }

        var trimmed = name.Trim();
        if (string.Equals(trimmed, UncategorizedLabel, StringComparison.OrdinalIgnoreCase))
        {
            return new CategoryMatch(CategoryMatchKind.Uncategorized, null);
        }

        var categories = await db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var match = categories.FirstOrDefault(
            c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? new CategoryMatch(CategoryMatchKind.Unknown, null)
            : new CategoryMatch(CategoryMatchKind.Resolved, match.Id);
    }

    /// <summary>Applies a resolved category filter to a transaction query.</summary>
    private protected static IQueryable<Transaction> ApplyCategory(IQueryable<Transaction> query, CategoryMatch match) =>
        match.Kind switch
        {
            CategoryMatchKind.Resolved => query.Where(t => t.CategoryId == match.Id),
            CategoryMatchKind.Uncategorized => query.Where(t => t.CategoryId == null),
            _ => query,
        };

    private protected enum CategoryMatchKind
    {
        NoFilter,
        Resolved,
        Uncategorized,
        Unknown,
    }

    private protected readonly record struct CategoryMatch(CategoryMatchKind Kind, Guid? Id);
}
