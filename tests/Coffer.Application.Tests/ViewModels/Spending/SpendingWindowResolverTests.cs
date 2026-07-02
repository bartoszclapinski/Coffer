using Coffer.Application.ViewModels.Spending;
using Coffer.Core.Spending;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Spending;

public class SpendingWindowResolverTests
{
    private static readonly DateOnly _today = new(2026, 7, 15);

    [Fact]
    public void ThisMonth_RunsFromTheFirstToToday() =>
        SpendingWindowResolver.Resolve(SpendingWindowPreset.ThisMonth, _today, null, null)
            .Should().Be(new SpendingWindow(new DateOnly(2026, 7, 1), _today));

    [Fact]
    public void LastMonth_SnapsToThePreviousCalendarMonth() =>
        SpendingWindowResolver.Resolve(SpendingWindowPreset.LastMonth, _today, null, null)
            .Should().Be(new SpendingWindow(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

    [Fact]
    public void Last3Months_RollsBackFromToday() =>
        SpendingWindowResolver.Resolve(SpendingWindowPreset.Last3Months, _today, null, null)
            .Should().Be(new SpendingWindow(new DateOnly(2026, 4, 15), _today));

    [Fact]
    public void ThisYear_RunsFromJanuaryFirst() =>
        SpendingWindowResolver.Resolve(SpendingWindowPreset.ThisYear, _today, null, null)
            .Should().Be(new SpendingWindow(new DateOnly(2026, 1, 1), _today));

    [Fact]
    public void Custom_UsesOwnerDates() =>
        SpendingWindowResolver.Resolve(
                SpendingWindowPreset.Custom, _today, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 20))
            .Should().Be(new SpendingWindow(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 20)));

    [Fact]
    public void Custom_InvertedRange_IsSwapped() =>
        SpendingWindowResolver.Resolve(
                SpendingWindowPreset.Custom, _today, new DateOnly(2026, 2, 20), new DateOnly(2026, 2, 1))
            .Should().Be(new SpendingWindow(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 20)));

    [Fact]
    public void Custom_MissingDates_FallBackToToday() =>
        SpendingWindowResolver.Resolve(SpendingWindowPreset.Custom, _today, null, null)
            .Should().Be(new SpendingWindow(_today, _today));
}
