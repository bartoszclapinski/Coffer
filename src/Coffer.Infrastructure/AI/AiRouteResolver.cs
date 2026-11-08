using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Reads a role's provider + model from <see cref="IAiSettings"/> on every call, so a route changed
/// in Settings applies to the next AI call. Normalisation of retired providers / models lives in the
/// settings store, so the Settings screen and the consumers see the same route. A route without a model
/// (a provider picked before any model was chosen) throws <see cref="AiRouteNotConfiguredException"/>
/// here, before any consumer prices or sends it.
/// </summary>
public sealed class AiRouteResolver : IAiRouteResolver
{
    private readonly IAiSettings _settings;

    public AiRouteResolver(IAiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public async Task<AiRoute> ResolveAsync(AiRole role, CancellationToken ct)
    {
        var route = await _settings.GetRouteAsync(role, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(route.Provider) || string.IsNullOrWhiteSpace(route.Model))
        {
            throw new AiRouteNotConfiguredException(role);
        }

        return route;
    }
}
