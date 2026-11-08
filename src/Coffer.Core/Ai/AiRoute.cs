namespace Coffer.Core.Ai;

/// <summary>
/// The provider and model one <see cref="AiRole"/> runs on, resolved from settings per call by
/// <see cref="IAiRouteResolver"/>. <see cref="Provider"/> names a registered provider
/// (<see cref="AiDefaults.ClaudeProvider"/>, <see cref="AiDefaults.OllamaProvider"/>); a consumer sends
/// both values on its request and prices its call with both.
/// </summary>
public sealed record AiRoute(string Provider, string Model);
