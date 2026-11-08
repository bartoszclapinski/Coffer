using System.Security.Cryptography;
using System.Text;
using Coffer.Core.Domain;
using FluentAssertions;

namespace Coffer.Core.Tests.Domain;

public class TransactionHashTests
{
    private const string Account = "PL61109010140000071219812874";

    [Fact]
    public void Compute_SameInputs_ProducesSameHash()
    {
        var date = new DateOnly(2025, 11, 28);
        var hashA = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 1234");
        var hashB = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 1234");

        hashA.Should().Be(hashB);
    }

    [Fact]
    public void Compute_DifferentAccount_ChangesHash()
    {
        var date = new DateOnly(2025, 11, 28);
        var hashA = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 1234");
        var hashB = TransactionHash.Compute("PL00000000000000000000000000", date, -49.99m, "BIEDRONKA 1234");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_DifferentDate_ChangesHash()
    {
        var hashA = TransactionHash.Compute(Account, new DateOnly(2025, 11, 28), -49.99m, "BIEDRONKA 1234");
        var hashB = TransactionHash.Compute(Account, new DateOnly(2025, 11, 27), -49.99m, "BIEDRONKA 1234");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_DifferentAmount_ChangesHash()
    {
        var date = new DateOnly(2025, 11, 28);
        var hashA = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 1234");
        var hashB = TransactionHash.Compute(Account, date, -50.00m, "BIEDRONKA 1234");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_DifferentDescription_ChangesHash()
    {
        var date = new DateOnly(2025, 11, 28);
        var hashA = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 1234");
        var hashB = TransactionHash.Compute(Account, date, -49.99m, "BIEDRONKA 5678");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_ReturnsUppercaseHexOfLength64()
    {
        var hash = TransactionHash.Compute(Account, new DateOnly(2025, 11, 28), -49.99m, "X");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9A-F]+$");
    }

    [Fact]
    public void Compute_AmountFormattedInvariant_NoCultureLeakage()
    {
        // The hash composition uses F2 invariant formatting. A 1234.5m and a
        // 1234.50m must produce identical hashes regardless of how the running
        // culture would format the decimal.
        var date = new DateOnly(2025, 11, 28);
        var hashA = TransactionHash.Compute(Account, date, 1234.5m, "X");
        var hashB = TransactionHash.Compute(Account, date, 1234.50m, "X");

        hashA.Should().Be(hashB);
    }

    [Fact]
    public void Compute_LegacyComposition_IsUnchanged()
    {
        // Rows imported before occurrence indexing (Sprint 30) are stored with this exact
        // hash; changing it would make every re-import duplicate them.
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{Account}|2025-11-28|-49.99|BIEDRONKA 1234")));

        TransactionHash.Compute(Account, new DateOnly(2025, 11, 28), -49.99m, "BIEDRONKA 1234")
            .Should().Be(expected);
    }

    [Fact]
    public void Compute_FirstOccurrence_EqualsLegacyHash()
    {
        var date = new DateOnly(2025, 11, 28);

        TransactionHash.Compute(Account, date, -10.00m, "KAWIARNIA", occurrence: 1)
            .Should().Be(TransactionHash.Compute(Account, date, -10.00m, "KAWIARNIA"));
    }

    [Fact]
    public void Compute_LaterOccurrence_HashesBaseWithOrdinalSuffix()
    {
        var date = new DateOnly(2025, 11, 28);
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{Account}|2025-11-28|-10.00|KAWIARNIA|#2")));

        var second = TransactionHash.Compute(Account, date, -10.00m, "KAWIARNIA", occurrence: 2);
        var third = TransactionHash.Compute(Account, date, -10.00m, "KAWIARNIA", occurrence: 3);

        second.Should().Be(expected);
        second.Should().NotBe(TransactionHash.Compute(Account, date, -10.00m, "KAWIARNIA"));
        third.Should().NotBe(second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_OccurrenceBelowOne_Throws(int occurrence)
    {
        var act = () => TransactionHash.Compute(Account, new DateOnly(2025, 11, 28), -10.00m, "X", occurrence);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
