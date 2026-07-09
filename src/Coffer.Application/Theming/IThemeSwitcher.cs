using Coffer.Core.Theming;

namespace Coffer.Application.Theming;

/// <summary>
/// Switches the UI theme at runtime and persists the choice. The concrete implementation
/// lives in the desktop layer (it touches Avalonia's <c>Application</c>); view-models depend
/// on this abstraction so the command palette / top-bar toggle stay framework-free.
/// </summary>
public interface IThemeSwitcher
{
    AppTheme Current { get; }

    void Toggle();

    void Set(AppTheme theme);

    /// <summary>Raised after the active theme changes so listeners can refresh (e.g. the toggle icon).</summary>
    event EventHandler? Changed;
}
