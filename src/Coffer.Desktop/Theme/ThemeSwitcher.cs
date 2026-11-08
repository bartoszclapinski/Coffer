using Coffer.Application.Theming;
using Coffer.Core.Theming;

namespace Coffer.Desktop.Theme;

/// <summary>
/// Desktop <see cref="IThemeSwitcher"/>: applies the variant to the running application via
/// <see cref="ThemeManager"/> and persists it through <see cref="IThemeStore"/> so the choice
/// survives a restart (and is honoured pre-login).
/// </summary>
public sealed class ThemeSwitcher : IThemeSwitcher
{
    private readonly IThemeStore _store;

    public ThemeSwitcher(IThemeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public AppTheme Current => ThemeManager.Current;

    public event EventHandler? Changed;

    public void Toggle() => Set(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    public void Set(AppTheme theme)
    {
        // No-op when unchanged: listeners that mirror the theme (e.g. the Settings toggle)
        // echo it back here, and that echo must not re-apply, re-save or re-raise Changed.
        if (theme == Current)
        {
            return;
        }

        ThemeManager.Apply(theme);
        _store.Save(theme);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
