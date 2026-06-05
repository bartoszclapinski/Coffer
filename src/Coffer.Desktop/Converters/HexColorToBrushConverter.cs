using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Coffer.Desktop.Converters;

/// <summary>
/// Turns a category colour hex string (e.g. <c>#534AB7</c>) into a brush for the colour
/// chip. An unset or unparsable value falls back to a neutral grey so a missing colour
/// never throws in the UI.
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    private static readonly IBrush _fallback = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex)
            && Color.TryParse(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return _fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
