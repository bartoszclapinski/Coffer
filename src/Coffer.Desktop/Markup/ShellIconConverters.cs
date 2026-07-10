using System.Globalization;
using Avalonia.Data.Converters;

namespace Coffer.Desktop.Markup;

/// <summary>
/// Small bool → Phosphor-glyph converters for the top-bar toggles: the theme button shows a
/// sun in dark mode (tap to go light) / a moon in light mode; the privacy button shows a
/// slashed eye when balances are hidden.
/// </summary>
public static class ShellIconConverters
{
    public static readonly IValueConverter Theme =
        new FuncValueConverter<bool, string>(isDark => PhosphorIcon.Glyph(isDark ? "sun" : "moon"));

    public static readonly IValueConverter Privacy =
        new FuncValueConverter<bool, string>(hidden => PhosphorIcon.Glyph(hidden ? "eye-slash" : "eye"));
}
