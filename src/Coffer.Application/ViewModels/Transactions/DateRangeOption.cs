namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// A selectable window for the transactions date filter. <see cref="Months"/> is the
/// look-back from today; a null value means "no lower bound" (the whole history).
/// <see cref="LabelKey"/> is a resource key resolved to display text in the view via
/// <c>LocalizeKeyConverter</c>, so the options stay framework-free here.
/// </summary>
public sealed record DateRangeOption(string LabelKey, int? Months)
{
    public static readonly DateRangeOption ThreeMonths = new("Transactions.Range.Last3Months", 3);

    public static readonly DateRangeOption SixMonths = new("Transactions.Range.Last6Months", 6);

    public static readonly DateRangeOption TwelveMonths = new("Transactions.Range.Last12Months", 12);

    public static readonly DateRangeOption All = new("Transactions.Range.All", null);

    public static readonly IReadOnlyList<DateRangeOption> Options =
        [ThreeMonths, SixMonths, TwelveMonths, All];

    public override string ToString() => LabelKey;
}
