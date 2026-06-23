using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Deterministic Polish text helpers for the 13-A templated titles/descriptions.
/// User-facing strings are Polish (conventions.md); code/comments stay English.
/// </summary>
internal static class AnomalyFormatting
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Formats a złoty amount as e.g. "1 234,50 zł". The sign is dropped — callers pass magnitudes.</summary>
    public static string Pln(decimal amount) =>
        amount.ToString("N2", _polish) + " zł";

    /// <summary>Resolves a category display name, falling back to "Bez kategorii" for unknown/blank ids.</summary>
    public static string Category(AnomalyDetectionContext context, Guid? categoryId)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (categoryId is Guid id
            && context.CategoryNames.TryGetValue(id, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return "Bez kategorii";
    }

    /// <summary>Normalizes a merchant string into a stable dedup/grouping key (trimmed, upper-invariant).</summary>
    public static string MerchantKey(string merchant) =>
        merchant.Trim().ToUpperInvariant();
}
