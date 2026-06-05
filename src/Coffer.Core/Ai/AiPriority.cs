namespace Coffer.Core.Ai;

/// <summary>
/// How the budget gate treats a prospective AI call when the monthly cap is reached.
/// <see cref="Critical"/> work (categorisation during an import the user just triggered)
/// is allowed to exceed the cap with a warning; <see cref="Normal"/> work is blocked.
/// </summary>
public enum AiPriority
{
    Normal = 0,
    Critical = 1,
}
