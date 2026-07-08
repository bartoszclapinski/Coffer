namespace Coffer.Core.Theming;

/// <summary>
/// The UI colour theme. Maps to an Avalonia <c>ThemeVariant</c> at the desktop layer.
/// <see cref="Light"/> is the migration-era default (most screens are still hand-styled
/// with light-only colours); the default flips to <see cref="Dark"/> once every screen
/// consumes the design tokens.
/// </summary>
public enum AppTheme
{
    Light = 0,
    Dark = 1,
}
