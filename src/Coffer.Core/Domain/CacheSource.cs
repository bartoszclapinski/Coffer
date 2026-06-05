namespace Coffer.Core.Domain;

/// <summary>
/// How a <see cref="CategoryCache"/> entry's category was decided. The ordering
/// encodes precedence in the learning loop: a <see cref="Manual"/> entry overrides
/// an <see cref="Ai"/> or <see cref="Rule"/> one for the same key, because a human
/// correction is the strongest signal.
/// </summary>
public enum CacheSource
{
    Rule = 0,
    Ai = 1,
    Manual = 2,
}
