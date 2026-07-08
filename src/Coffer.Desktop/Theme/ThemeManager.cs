using Avalonia;
using Avalonia.Styling;
using Coffer.Core.Theming;

namespace Coffer.Desktop.Theme;

/// <summary>
/// Maps the framework-free <see cref="AppTheme"/> onto Avalonia's <see cref="ThemeVariant"/>
/// and applies it to the running application. Kept in the desktop layer because it touches
/// <see cref="Avalonia.Application"/>; <see cref="Apply"/> is called at startup (from the persisted
/// <c>IThemeStore</c>) and by the top-bar toggle / command palette to switch live.
/// </summary>
public static class ThemeManager
{
    public static ThemeVariant ToVariant(AppTheme theme) =>
        theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    public static AppTheme FromVariant(ThemeVariant? variant) =>
        variant == ThemeVariant.Dark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>The theme currently applied to the running application (Light if unset).</summary>
    public static AppTheme Current =>
        FromVariant(Avalonia.Application.Current?.RequestedThemeVariant);

    /// <summary>Applies <paramref name="theme"/> to the running application (no persistence).</summary>
    public static void Apply(AppTheme theme)
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = ToVariant(theme);
        }
    }
}
