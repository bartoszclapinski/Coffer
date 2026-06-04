using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Coffer.Desktop.Converters;

/// <summary>
/// Paints positive amounts in the income green and everything else in the primary
/// text colour, matching the transactions mockup where expenses read as normal text.
/// </summary>
public sealed class AmountToBrushConverter : IValueConverter
{
    public static readonly AmountToBrushConverter Instance = new();

    private static readonly IBrush _income = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush _default = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal amount && amount > 0 ? _income : _default;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
