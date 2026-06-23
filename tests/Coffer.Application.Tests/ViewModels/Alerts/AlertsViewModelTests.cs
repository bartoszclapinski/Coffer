using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Alerts;
using Coffer.Core.Anomalies;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Alerts;

public class AlertsViewModelTests
{
    private static AlertListItem Item(string title = "tytuł", decimal? amount = null) =>
        new(
            Guid.NewGuid(),
            AnomalyType.NewMerchant,
            title,
            "opis",
            amount,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            DateTime.UtcNow);

    [Fact]
    public async Task Load_RunsDetectionThenLoadsActiveAlerts()
    {
        var detect = new FakeDetectAnomaliesUseCase { Result = 2 };
        var query = new FakeAlertsQuery();
        query.Items.Add(Item("a"));
        query.Items.Add(Item("b"));
        var vm = new AlertsViewModel(detect, query, new FakeAlertService(), NullLogger<AlertsViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        detect.Calls.Should().Be(1);
        query.Calls.Should().Be(1);
        vm.Alerts.Should().HaveCount(2);
        vm.HasAlerts.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Load_NoAlerts_ReportsEmptyState()
    {
        var vm = new AlertsViewModel(
            new FakeDetectAnomaliesUseCase(),
            new FakeAlertsQuery(),
            new FakeAlertService(),
            NullLogger<AlertsViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasAlerts.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
        vm.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenDetectionThrows_SetsErrorAndClearsEmptyState()
    {
        var detect = new FakeDetectAnomaliesUseCase { Throw = new InvalidOperationException("boom") };
        var vm = new AlertsViewModel(detect, new FakeAlertsQuery(), new FakeAlertService(), NullLogger<AlertsViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.IsLoading.Should().BeFalse();
        vm.IsEmpty.Should().BeFalse("an error is its own state, not the empty state");
    }

    [Fact]
    public async Task Acknowledge_RemovesRowAndCallsService()
    {
        var query = new FakeAlertsQuery();
        query.Items.Add(Item("a"));
        var service = new FakeAlertService();
        var vm = new AlertsViewModel(new FakeDetectAnomaliesUseCase(), query, service, NullLogger<AlertsViewModel>.Instance);
        await vm.LoadCommand.ExecuteAsync(null);
        var row = vm.Alerts.Single();

        await row.AcknowledgeCommand.ExecuteAsync(null);

        service.Acknowledged.Should().ContainSingle().Which.Should().Be(row.Id);
        vm.Alerts.Should().BeEmpty();
        vm.HasAlerts.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Dismiss_RemovesRowAndCallsService()
    {
        var query = new FakeAlertsQuery();
        query.Items.Add(Item("a"));
        var service = new FakeAlertService();
        var vm = new AlertsViewModel(new FakeDetectAnomaliesUseCase(), query, service, NullLogger<AlertsViewModel>.Instance);
        await vm.LoadCommand.ExecuteAsync(null);
        var row = vm.Alerts.Single();

        await row.DismissCommand.ExecuteAsync(null);

        service.Dismissed.Should().ContainSingle().Which.Should().Be(row.Id);
        vm.Alerts.Should().BeEmpty();
    }

    [Fact]
    public void Row_FormatsAmountAndHidesItWhenAbsent()
    {
        var withAmount = new AlertRowViewModel(Item(amount: 1234.5m), _ => Task.CompletedTask, _ => Task.CompletedTask);
        var withoutAmount = new AlertRowViewModel(Item(), _ => Task.CompletedTask, _ => Task.CompletedTask);

        withAmount.HasAmount.Should().BeTrue();
        withAmount.AmountText.Should().Contain("zł");
        withoutAmount.HasAmount.Should().BeFalse();
        withoutAmount.AmountText.Should().BeEmpty();
    }
}
