using Coffer.Core.Anomalies;
using Coffer.Infrastructure.Anomalies.Detectors;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Anomalies;

public class AnomalyDetectorTests
{
    private static readonly Guid _groceries = Guid.NewGuid();
    private static readonly DateOnly _recentFrom = new(2026, 6, 1);
    private static readonly DateOnly _recentTo = new(2026, 6, 30);

    private static TransactionSnapshot Tx(
        DateOnly date,
        decimal amount,
        string? merchant = null,
        Guid? categoryId = null) =>
        new(Guid.NewGuid(), date, null, amount, merchant, merchant?.ToUpperInvariant() ?? "TX", categoryId);

    private static AnomalyDetectionContext Context(
        IReadOnlyList<TransactionSnapshot> recent,
        IReadOnlyList<TransactionSnapshot> baseline) =>
        new(
            recent,
            baseline,
            new Dictionary<Guid, string> { [_groceries] = "Spożywcze" },
            _recentFrom,
            _recentTo);

    [Fact]
    public void HighAmount_FlagsZScoreOutlierInCategory()
    {
        var baseline = new[] { 100m, 102m, 98m, 101m, 99m, 100m, 103m, 97m }
            .Select(m => Tx(new DateOnly(2026, 3, 1), -m, "Sklep", _groceries))
            .ToList();
        var recent = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 6, 10), -500m, "Sklep", _groceries) };

        var result = new HighAmountInCategoryDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().ContainSingle();
        result[0].Type.Should().Be(AnomalyType.HighAmountInCategory);
        result[0].RelatedAmount.Should().Be(500m);
        result[0].Score.Should().BeGreaterThan(3.0);
    }

    [Fact]
    public void HighAmount_StaysQuietBelowMinBaselineSamples()
    {
        var baseline = new[] { 100m, 102m, 98m }
            .Select(m => Tx(new DateOnly(2026, 3, 1), -m, "Sklep", _groceries))
            .ToList();
        var recent = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 6, 10), -500m, "Sklep", _groceries) };

        var result = new HighAmountInCategoryDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().BeEmpty("a sparse category must not fire");
    }

    [Fact]
    public void NewMerchant_FlagsUnseenMerchant()
    {
        var baseline = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 3, 1), -50m, "Stary") };
        var recent = new List<TransactionSnapshot>
        {
            Tx(new DateOnly(2026, 6, 5), -80m, "Nowy"),
            Tx(new DateOnly(2026, 6, 7), -90m, "nowy "),
        };

        var result = new NewMerchantDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().ContainSingle("the same merchant key is raised once per run");
        result[0].Type.Should().Be(AnomalyType.NewMerchant);
    }

    [Fact]
    public void NewMerchant_SkipsWhenBaselineEmpty()
    {
        var recent = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 6, 5), -80m, "Nowy") };

        var result = new NewMerchantDetector().Detect(Context(recent, [])).ToList();

        result.Should().BeEmpty("a fresh import would mark every merchant as new");
    }

    [Fact]
    public void CategorySpike_FlagsMonthlyTotalOutlier()
    {
        // Monthly totals 90 / 100 / 110 give a non-zero spread (mean 100); a flat baseline
        // would have std 0 and the detector would (correctly) stay silent.
        var monthAmounts = new Dictionary<int, decimal[]>
        {
            [3] = [-30m, -30m, -30m],
            [4] = [-30m, -35m, -35m],
            [5] = [-40m, -35m, -35m],
        };
        var baseline = new List<TransactionSnapshot>();
        foreach (var (month, amounts) in monthAmounts)
        {
            var day = 5;
            foreach (var amount in amounts)
            {
                baseline.Add(Tx(new DateOnly(2026, month, day), amount, "Sklep", _groceries));
                day += 10;
            }
        }

        var recent = new List<TransactionSnapshot>
        {
            Tx(new DateOnly(2026, 6, 5), -400m, "Sklep", _groceries),
            Tx(new DateOnly(2026, 6, 15), -400m, "Sklep", _groceries),
        };

        var result = new CategorySpikeDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().ContainSingle();
        result[0].Type.Should().Be(AnomalyType.CategorySpike);
        result[0].RelatedTransactionId.Should().BeNull();
        result[0].RelatedAmount.Should().Be(800m);
    }

    [Fact]
    public void DuplicatePayment_FlagsSameAmountSameMerchantAdjacentDay()
    {
        var recent = new List<TransactionSnapshot>
        {
            Tx(new DateOnly(2026, 6, 10), -120m, "Sklep"),
            Tx(new DateOnly(2026, 6, 11), -120m, "sklep"),
        };

        var result = new DuplicatePaymentDetector().Detect(Context(recent, [])).ToList();

        result.Should().ContainSingle();
        result[0].Type.Should().Be(AnomalyType.DuplicatePayment);
        result[0].RelatedAmount.Should().Be(120m);
    }

    [Fact]
    public void DuplicatePayment_IgnoresDistantDays()
    {
        var recent = new List<TransactionSnapshot>
        {
            Tx(new DateOnly(2026, 6, 1), -120m, "Sklep"),
            Tx(new DateOnly(2026, 6, 20), -120m, "Sklep"),
        };

        var result = new DuplicatePaymentDetector().Detect(Context(recent, [])).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void MissingRecurrence_FlagsRecurringMerchantAbsentFromRecent()
    {
        var baseline = new[] { 3, 4, 5 }
            .Select(month => Tx(new DateOnly(2026, month, 12), -40m, "Netflix"))
            .ToList();
        var recent = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 6, 5), -80m, "Inny") };

        var result = new MissingRecurrenceDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().ContainSingle();
        result[0].Type.Should().Be(AnomalyType.MissingRecurrence);
        result[0].RelatedAmount.Should().Be(40m);
    }

    [Fact]
    public void MissingRecurrence_QuietWhenMerchantStillPresent()
    {
        var baseline = new[] { 3, 4, 5 }
            .Select(month => Tx(new DateOnly(2026, month, 12), -40m, "Netflix"))
            .ToList();
        var recent = new List<TransactionSnapshot> { Tx(new DateOnly(2026, 6, 12), -40m, "Netflix") };

        var result = new MissingRecurrenceDetector().Detect(Context(recent, baseline)).ToList();

        result.Should().BeEmpty();
    }
}
