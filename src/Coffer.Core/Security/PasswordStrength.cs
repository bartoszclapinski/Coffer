namespace Coffer.Core.Security;

/// <summary>
/// Strength evaluation of a candidate master password. <see cref="Score"/> follows the
/// zxcvbn convention: 0 = too guessable, 1 = very guessable, 2 = somewhat guessable,
/// 3 = safely unguessable, 4 = very unguessable.
/// </summary>
/// <remarks>
/// <see cref="Warning"/> and <see cref="Suggestions"/> come from the underlying library
/// in English. The Sprint 5 UI shows only <see cref="Score"/> as a progress bar plus
/// a static Polish hint; localising the warning codes is a separate follow-up chore.
/// </remarks>
public sealed record PasswordStrength(int Score, string? Warning, IReadOnlyList<string> Suggestions);
