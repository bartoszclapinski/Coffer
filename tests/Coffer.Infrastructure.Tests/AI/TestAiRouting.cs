using Coffer.Core.Ai;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// A route resolver over fixed routes (the Claude defaults unless given), recording every role asked
/// for so a consumer test can assert which role it resolved.
/// </summary>
public sealed class FixedRouteResolver : IAiRouteResolver
{
    private readonly AiRoute _categorization;
    private readonly AiRoute _analysis;

    public FixedRouteResolver(AiRoute? categorization = null, AiRoute? analysis = null)
    {
        _categorization = categorization ?? new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.CategorizationModel);
        _analysis = analysis ?? new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.AnalysisModel);
    }

    /// <summary>A resolver that answers <paramref name="route"/> for every role.</summary>
    public static FixedRouteResolver Always(AiRoute route) => new(route, route);

    public List<AiRole> ResolvedRoles { get; } = [];

    public Task<AiRoute> ResolveAsync(AiRole role, CancellationToken ct)
    {
        ResolvedRoles.Add(role);
        return Task.FromResult(role == AiRole.Categorization ? _categorization : _analysis);
    }
}

/// <summary>
/// A resolver whose role has no model chosen, as the real <c>AiRouteResolver</c> reports it: every
/// call throws <see cref="AiRouteNotConfiguredException"/>.
/// </summary>
public sealed class NotConfiguredRouteResolver : IAiRouteResolver
{
    public Task<AiRoute> ResolveAsync(AiRole role, CancellationToken ct) =>
        Task.FromException<AiRoute>(new AiRouteNotConfiguredException(role));
}

/// <summary>Readiness with a fixed answer, for consumer tests that are not about readiness.</summary>
public sealed class FixedRouteReadiness(bool isReady) : IAiRouteReadiness
{
    public List<AiRoute> Checked { get; } = [];

    public Task<bool> IsReadyAsync(AiRoute route, CancellationToken ct)
    {
        Checked.Add(route);
        return Task.FromResult(isReady);
    }
}

/// <summary>
/// Pricing at a fixed small cost that records the provider + model of every estimate, so a consumer
/// test can assert the routed provider and model are what the budget gate was priced with.
/// </summary>
public sealed class RecordingPricing : IAiPricing
{
    public List<(string Provider, string Model)> Estimates { get; } = [];

    public List<(string Provider, string Model)> PreCallEstimates { get; } = [];

    public AiCost Estimate(string provider, string model, int inputTokens, int outputTokens)
    {
        Estimates.Add((provider, model));
        return new AiCost(0.01m, 0.04m);
    }

    public AiCost EstimateBeforeCall(string provider, string model, int inputTokens, int answerTokens)
    {
        PreCallEstimates.Add((provider, model));
        return new AiCost(0.01m, 0.04m);
    }
}

/// <summary>A non-default route used to prove a consumer sends what the resolver returns, not a default.</summary>
public static class TestRoutes
{
    public static AiRoute Routed { get; } = new("RoutedVendor", "routed-model-1");

    /// <summary>
    /// A local provider with no model chosen (the store's "not chosen yet" state). Real
    /// <c>AiPricing</c> rejects its empty model, so a consumer must not let that escape.
    /// </summary>
    public static AiRoute OllamaWithoutModel { get; } = new(AiDefaults.OllamaProvider, string.Empty);
}
