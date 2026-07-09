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
        ThemeManager.Apply(theme);
        _store.Save(theme);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
