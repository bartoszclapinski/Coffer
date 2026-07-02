using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Planning;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Planning;

public class AffordabilityViewModelTests
{
    private static readonly DateTimeOffset _spendDate = new(new DateTime(2026, 1, 5), TimeSpan.Zero);

    [Fact]
    public async Task Check_AnchoredAccount_SurfacesGroundedVerdictWithDriver()
    {
        var accountId = Guid.NewGuid();
        var accounts = new FakeAccountService();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", new DateOnly(2026, 1, 1), 5000m);
        var vm = Create(
            out var balance, out _, out _, accounts,
            Flow("Salary", FlowDirection.Inflow, 6000m, day: 25),
            Flow("Rent", FlowDirection.Outflow, 2000m, day: 10));
        balance.Balance = 5000m;

        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedAccount = vm.Accounts.First(a => a.Id == accountId);
        vm.Amount = 1000m;
        vm.SpendDate = _spendDate;
        await vm.CheckCommand.ExecuteAsync(null);

        vm.HasResult.Should().BeTrue();
        vm.CanAfford.Should().BeTrue();
        vm.IsRelative.Should().BeFalse();
        vm.IsUncertain.Should().BeFalse();
        vm.HasDriver.Should().BeTrue();
        // The fake localizer echoes keys rather than interpolating, so assert the key resolved, not the
        // driver name; the interpolation itself is covered by the real resx (parity test) and the engine.
        vm.DriverText.Should().NotBeNullOrEmpty();
        vm.LowestBalanceText.Should().NotBeNullOrEmpty();
        vm.HeadroomText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Check_AllAccounts_IsFlaggedRelative()
    {
        var vm = Create(out var balance, out _, out _, new FakeAccountService(),
            Flow("Salary", FlowDirection.Inflow, 6000m, day: 25));
        balance.Balance = 3000m;

        await vm.LoadCommand.ExecuteAsync(null);
        // The default selection is the "all accounts" option (id null).
        vm.Amount = 100m;
        vm.SpendDate = _spendDate;
        await vm.CheckCommand.ExecuteAsync(null);

        vm.HasResult.Should().BeTrue();
        vm.IsRelative.Should().BeTrue();
    }

    [Fact]
    public async Task Check_WithZeroAmount_ShowsErrorAndNoResult()
    {
        var vm = Create(out _, out _, out _, new FakeAccountService());

        await vm.LoadCommand.ExecuteAsync(null);
        vm.Amount = 0m;
        await vm.CheckCommand.ExecuteAsync(null);

        vm.HasResult.Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Check_WhenTrustReportsGap_IsFlaggedUncertain()
    {
        var accountId = Guid.NewGuid();
        var accounts = new FakeAccountService();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", new DateOnly(2026, 1, 1), 5000m);
        var vm = Create(out var balance, out _, out var trust, accounts);
        balance.Balance = 5000m;
        trust.IsTrustworthy = false;
        trust.Gaps.Add(new StatementGap(accountId, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 4)));

        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedAccount = vm.Accounts.First(a => a.Id == accountId);
        vm.Amount = 100m;
        vm.SpendDate = _spendDate;
        await vm.CheckCommand.ExecuteAsync(null);

        vm.IsUncertain.Should().BeTrue();
        vm.UncertaintyText.Should().NotBeNullOrEmpty();
    }

    private static AffordabilityViewModel Create(
        out FakeRunningBalanceQuery balance,
        out FakeVariableBurnQuery burn,
        out FakeBalanceTrustQuery trust,
        FakeAccountService accounts,
        params RecurringFlow[] flows)
    {
        balance = new FakeRunningBalanceQuery();
        burn = new FakeVariableBurnQuery();
        trust = new FakeBalanceTrustQuery();
        return new AffordabilityViewModel(
            new AffordabilityEngine(new CashFlowProjectionEngine()),
            balance,
            burn,
            trust,
            new FakePlanningSettings(),
            new FakeRecurringFlowRepository(flows),
            accounts,
            new FakeLocalizer(),
            NullLogger<AffordabilityViewModel>.Instance);
    }

    private static RecurringFlow Flow(string name, FlowDirection direction, decimal amount, int day) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Direction = direction,
        IntervalMonths = 1,
        AnchorDayOfMonth = day,
        TypicalAmount = amount,
        Currency = "PLN",
        IsActive = true,
        Source = FlowSource.Manual,
        CreatedAt = DateTime.UtcNow,
    };
}
