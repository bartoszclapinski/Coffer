namespace Coffer.Core.Ai;

/// <summary>
/// Resolves the provider + model an <see cref="AiRole"/> currently runs on. Every AI consumer asks it
/// on each call (never caching the answer), so a change in Settings applies to the next call without a
/// restart, and no consumer hard-codes a provider or model.
/// </summary>
public interface IAiRouteResolver
{
    Task<AiRoute> ResolveAsync(AiRole role, CancellationToken ct);
}
