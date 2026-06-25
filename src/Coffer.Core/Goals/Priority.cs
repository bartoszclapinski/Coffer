namespace Coffer.Core.Goals;

/// <summary>
/// Owner-set importance of a goal (doc 07). Higher-priority goals are funded first when the
/// engine apportions free cash across competing goals. Persisted as the enum name.
/// </summary>
public enum Priority
{
    Low = 1,
    Medium = 2,
    High = 3,
}
