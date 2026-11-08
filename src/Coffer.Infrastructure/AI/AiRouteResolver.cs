using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Reads a role's provider + model from <see cref="IAiSettings"/> on every call, so a route changed
/// in Settings applies to the next AI call. Normalisation of retired providers / models lives in the
/// settings store, so the Settings screen and the consumers see the same route.
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
        var provider = await _settings.GetProviderAsync(role, ct).ConfigureAwait(false);
        var model = await _settings.GetModelAsync(role, ct).ConfigureAwait(false);
        return new AiRoute(provider, model);
    }
}
