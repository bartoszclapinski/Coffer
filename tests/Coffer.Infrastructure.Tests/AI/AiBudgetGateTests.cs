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

        public Task<string> GetActiveProviderAsync(CancellationToken ct) =>
            Task.FromResult(AiDefaults.ClaudeProvider);

        public Task SetActiveProviderAsync(string provider, CancellationToken ct) => Task.CompletedTask;

        public Task<string> GetCategorizationModelAsync(CancellationToken ct) =>
            Task.FromResult(AiDefaults.CategorizationModel);

        public Task SetCategorizationModelAsync(string model, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct) =>
            Task.FromResult(AiDefaults.AiFallbackParsingEnabled);

        public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;

        public Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct) => Task.CompletedTask;
    }
}
