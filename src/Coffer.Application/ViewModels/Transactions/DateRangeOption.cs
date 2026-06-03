namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// A selectable window for the transactions date filter. <see cref="Months"/> is the
/// look-back from today; a null value means "no lower bound" (the whole history).
/// </summary>
public sealed record DateRangeOption(string Label, int? Months)
{
    public static readonly DateRangeOption ThreeMonths = new("Ostatnie 3 mies.", 3);

    public static readonly DateRangeOption SixMonths = new("Ostatnie 6 mies.", 6);

    public static readonly DateRangeOption TwelveMonths = new("Ostatnie 12 mies.", 12);

    public static readonly DateRangeOption All = new("Cały okres", null);

    public static readonly IReadOnlyList<DateRangeOption> Options =
        [ThreeMonths, SixMonths, TwelveMonths, All];

    public override string ToString() => Label;
}
