using Coffer.Core.Spending;

namespace Coffer.Application.ViewModels.Spending;

/// <summary>The selectable time windows for the spending explorer.</summary>
public enum SpendingWindowPreset
{
    ThisMonth,
    LastMonth,
    Last3Months,
    Last12Months,
    ThisYear,
    Custom,
}

/// <summary>The drill-down levels the explorer walks through.</summary>
public enum SpendingDrillLevel
{
    Categories,
    Merchants,
    Transactions,
}

/// <summary>
/// Pure resolution of a <see cref="SpendingWindowPreset"/> into a concrete inclusive
/// <see cref="SpendingWindow"/>, given a reference date (kept a parameter so the mapping is
/// deterministically testable without a clock). Rolling presets end on <paramref name="today"/>;
/// calendar presets snap to month/year boundaries; <see cref="SpendingWindowPreset.Custom"/> uses the
/// owner's dates (defaulting a missing end to today and a missing start to the same day, and swapping an
/// inverted range so the window is always well-formed).
/// </summary>
public static class SpendingWindowResolver
{
    public static SpendingWindow Resolve(
        SpendingWindowPreset preset, DateOnly today, DateOnly? customFrom, DateOnly? customTo)
    {
        switch (preset)
        {
            case SpendingWindowPreset.ThisMonth:
                return new SpendingWindow(new DateOnly(today.Year, today.Month, 1), today);
            case SpendingWindowPreset.LastMonth:
                var firstOfThis = new DateOnly(today.Year, today.Month, 1);
                var firstOfLast = firstOfThis.AddMonths(-1);
                return new SpendingWindow(firstOfLast, firstOfThis.AddDays(-1));
            case SpendingWindowPreset.Last3Months:
                return new SpendingWindow(today.AddMonths(-3), today);
            case SpendingWindowPreset.Last12Months:
                return new SpendingWindow(today.AddMonths(-12), today);
            case SpendingWindowPreset.ThisYear:
                return new SpendingWindow(new DateOnly(today.Year, 1, 1), today);
            case SpendingWindowPreset.Custom:
            default:
                var to = customTo ?? today;
                var from = customFrom ?? to;
                return from <= to ? new SpendingWindow(from, to) : new SpendingWindow(to, from);
        }
    }
}
