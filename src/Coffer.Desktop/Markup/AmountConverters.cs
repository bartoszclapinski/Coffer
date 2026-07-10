using Avalonia.Data.Converters;

namespace Coffer.Desktop.Markup;

/// <summary>
/// Sign predicates for monetary amounts, used to toggle the theme-aware <c>money.pos</c> /
/// <c>money.neg</c> style classes (green / red tokens) instead of a fixed-colour brush — so
/// amounts stay readable in both light and dark (a fixed near-black expense colour was
/// invisible on the dark panel).
/// </summary>
public static class AmountConverters
{
    public static readonly IValueConverter IsPositive = new FuncValueConverter<decimal, bool>(a => a > 0m);

    public static readonly IValueConverter IsNegative = new FuncValueConverter<decimal, bool>(a => a < 0m);
}
