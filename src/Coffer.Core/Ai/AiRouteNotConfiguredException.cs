namespace Coffer.Core.Ai;

/// <summary>
/// Thrown by <see cref="IAiRouteResolver"/> when a role's stored route cannot be used as it stands — no
/// model has been chosen yet (e.g. a local provider picked before any of its models was listed).
/// Consumers treat it like <see cref="MissingAiKeyException"/> and <see cref="UnknownAiProviderException"/>:
/// the AI feature is not configured, nothing is sent, and the feature degrades instead of failing.
/// </summary>
public sealed class AiRouteNotConfiguredException : Exception
{
    public AiRouteNotConfiguredException(AiRole role)
        : base($"No AI model is chosen for the {role} role. Choose one in Settings.")
    {
        Role = role;
    }

    /// <summary>The role whose route is not configured.</summary>
    public AiRole Role { get; }
}
