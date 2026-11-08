using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.AI;

public class AiBudgetGateTests
{
    [Fact]
    public async Task CanProceed_WhenUnderCap_ReturnsTrue()
    {
        var gate = CreateGate(spent: 5m, cap: 20m);

        var ok = await gate.CanProceedAsync(estimatedCostPln: 1m, AiPriority.Normal, CancellationToken.None);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task CanProceed_WhenOverCapAndNormal_ReturnsFalse()
    {
        var gate = CreateGate(spent: 20m, cap: 20m);

        var ok = await gate.CanProceedAsync(estimatedCostPln: 1m, AiPriority.Normal, CancellationToken.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task CanProceed_WhenOverCapButCritical_ReturnsTrue()
    {
        var gate = CreateGate(spent: 20m, cap: 20m);

        var ok = await gate.CanProceedAsync(estimatedCostPln: 1m, AiPriority.Critical, CancellationToken.None);

        ok.Should().BeTrue("Critical work bypasses the cap (e.g. categorising an import the user just asked for)");
    }

    [Theory]
    [InlineData(AiPriority.Normal)]
    [InlineData(AiPriority.Critical)]
    public async Task CanProceed_ZeroCostCallPastTheCap_ReturnsTrue(AiPriority priority)
    {
        var gate = CreateGate(spent: 50m, cap: 20m);

        var ok = await gate.CanProceedAsync(estimatedCostPln: 0m, priority, CancellationToken.None);

        ok.Should().BeTrue("a local model costs nothing, so the cap has nothing to protect");
    }

    [Fact]
    public async Task CanProceed_SmallestPaidCallPastTheCap_StillBlocksNormalWork()
    {
        var gate = CreateGate(spent: 20m, cap: 20m);

        var ok = await gate.CanProceedAsync(estimatedCostPln: 0.0001m, AiPriority.Normal, CancellationToken.None);

        ok.Should().BeFalse("only a zero estimate bypasses the cap");
    }

    private static AiBudgetGate CreateGate(decimal spent, decimal cap) =>
        new(new StubLedger(spent), new StubSettings(cap), NullLogger<AiBudgetGate>.Instance);

    private sealed class StubLedger(decimal spent) : IAiUsageLedger
    {
        public Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct) => Task.CompletedTask;

        public Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct) => Task.FromResult(spent);

        public Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiSpendByPurpose>>([]);
    }

    private sealed class StubSettings(decimal cap) : IAiSettings
    {
        public Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct) => Task.FromResult(cap);

        public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct) => Task.CompletedTask;

        public Task<AiRoute> GetRouteAsync(AiRole role, CancellationToken ct) =>
            Task.FromResult(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.DefaultModel(role)));

        public Task SetRouteAsync(AiRole role, AiRoute route, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct) =>
            Task.FromResult(AiDefaults.AiFallbackParsingEnabled);

        public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;

        public Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct) => Task.CompletedTask;
    }
}
