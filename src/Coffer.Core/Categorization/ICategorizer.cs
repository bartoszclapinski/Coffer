namespace Coffer.Core.Categorization;

/// <summary>
/// Resolves a category for each distinct normalised description. The Phase 10-A
/// implementation is deterministic (cache → rules); Phase 10-C swaps in a hybrid
/// that adds an AI batch for the leftovers — both behind this same abstraction, so
/// the import flow does not change. Unknown descriptions map to <c>null</c>.
/// </summary>
public interface ICategorizer
{
    Task<IReadOnlyDictionary<string, Guid?>> CategorizeAsync(
        IReadOnlyCollection<string> normalizedDescriptions,
        CancellationToken ct);
}
