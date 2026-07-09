using System.Globalization;
using Avalonia.Data.Converters;

namespace Coffer.Desktop.Markup;

/// <summary>
/// Converts a bound Phosphor icon <em>name</em> (e.g. a <c>NavItem.Icon</c> string) to its glyph,
/// for use where the <see cref="PhosphorIcon"/> markup extension can't (data-bound values in an
/// <c>ItemsControl</c> template). Pair with the <c>Coffer.Icons</c> / <c>Coffer.IconsFill</c> font.
/// </summary>
public sealed class PhosphorGlyphConverter : IValueConverter
{
    public static readonly PhosphorGlyphConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? PhosphorIcon.Glyph(name) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
