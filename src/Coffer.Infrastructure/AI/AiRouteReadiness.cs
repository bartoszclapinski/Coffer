using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Answers whether a route can be used without calling it: its provider is registered, a model is
/// set, and the provider reports its own prerequisites met (<see cref="IAiProviderBackend.IsConfiguredAsync"/>
/// — Claude needs a stored API key, a local provider needs nothing).
/// </summary>
public sealed class AiRouteReadiness : IAiRouteReadiness
{
    private readonly IReadOnlyList<IAiProviderBackend> _backends;

    public AiRouteReadiness(IEnumerable<IAiProviderBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        _backends = backends.ToList();
    }

    public async Task<bool> IsReadyAsync(AiRoute route, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (string.IsNullOrWhiteSpace(route.Model))
        {
            return false;
        }

        var backend = _backends.FirstOrDefault(
            b => string.Equals(b.ProviderName, route.Provider, StringComparison.OrdinalIgnoreCase));
        return backend is not null && await backend.IsConfiguredAsync(ct).ConfigureAwait(false);
    }
}
