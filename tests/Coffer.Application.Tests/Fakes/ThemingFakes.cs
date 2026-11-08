using Coffer.Application.Theming;
using Coffer.Core.Theming;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IThemeSwitcher"/>: records toggles and raises <see cref="Changed"/>.</summary>
internal sealed class FakeThemeSwitcher : IThemeSwitcher
{
    public AppTheme Current { get; private set; } = AppTheme.Light;

    public int ToggleCalls { get; private set; }

    /// <summary>Number of effective (state-changing) Set calls, i.e. theme applies + saves.</summary>
    public int AppliedCount { get; private set; }

    public FakeThemeSwitcher(AppTheme initial = AppTheme.Light) => Current = initial;

    public event EventHandler? Changed;

    public void Toggle()
    {
        ToggleCalls++;
        Set(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }

    public void Set(AppTheme theme)
    {
        // Mirrors the real ThemeSwitcher: unchanged theme is a no-op.
        if (theme == Current)
        {
            return;
        }

        AppliedCount++;
        Current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
