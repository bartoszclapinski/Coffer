using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Coffer.Application.Localization;

namespace Coffer.Desktop.Converters;

/// <summary>
/// Resolves a resource-key string carried by a bound item (e.g. <c>DateRangeOption.LabelKey</c>
/// or a filter sentinel's name) to the localized text via the singleton <see cref="ILocalizer"/>.
/// Unlike <c>{l:Localize}</c>, this works for keys that live in data rather than XAML. A value that
/// is not a known key resolves to itself (the localizer returns the key when no entry exists), so
/// real, already-localized item text passes through untouched. Re-evaluates on the next item-source
/// load after a language switch.
/// </summary>
public sealed class LocalizeKeyConverter : IValueConverter
{
    public static readonly LocalizeKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return value;
        }

        return App.Services?.GetService(typeof(ILocalizer)) is ILocalizer localizer
            ? localizer[key]
            : key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
