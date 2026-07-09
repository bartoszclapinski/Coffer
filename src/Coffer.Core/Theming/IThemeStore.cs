namespace Coffer.Core.Theming;

/// <summary>
/// Persists the owner's chosen UI theme. Stored as a small plaintext file (not the
/// encrypted DB) because the choice is non-sensitive and must be readable on the
/// pre-login screens (setup wizard, login) before the DEK exists — mirroring
/// <c>ILanguageStore</c>. Reads default to <see cref="AppTheme.Light"/> until the
/// redesign migration is complete, after which the default flips to dark.
/// </summary>
public interface IThemeStore
{
    AppTheme Load();

    void Save(AppTheme theme);
}
