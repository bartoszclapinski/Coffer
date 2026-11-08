namespace Coffer.Core.Ai;

/// <summary>
/// The one place that answers "can this route be used right now?" without calling the provider: the
/// provider is registered, a model is set, and the provider's own prerequisites are met (Claude needs
/// an API key; a local provider needs none). Used where a feature must decide up front whether it is
/// available, such as the AI fallback parser.
/// </summary>
public interface IAiRouteReadiness
{
    Task<bool> IsReadyAsync(AiRoute route, CancellationToken ct);
}
