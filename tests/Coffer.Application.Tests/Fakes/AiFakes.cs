using System.Collections.Concurrent;
using Coffer.Core.Ai;
using Coffer.Core.Security;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IAiSettings"/> seeded from <see cref="AiDefaults"/>.</summary>
internal sealed class FakeAiSettings : IAiSettings
{
    public decimal MonthlyCapPln { get; set; } = AiDefaults.MonthlyCapPln;

    public string ActiveProvider { get; set; } = AiDefaults.ClaudeProvider;

    public string CategorizationModel { get; set; } = AiDefaults.CategorizationModel;

    public Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct) => Task.FromResult(MonthlyCapPln);

    public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct)
    {
        MonthlyCapPln = capPln;
        return Task.CompletedTask;
    }

    public Task<string> GetActiveProviderAsync(CancellationToken ct) => Task.FromResult(ActiveProvider);

    public Task SetActiveProviderAsync(string provider, CancellationToken ct)
    {
        ActiveProvider = provider;
        return Task.CompletedTask;
    }

    public Task<string> GetCategorizationModelAsync(CancellationToken ct) => Task.FromResult(CategorizationModel);

    public Task SetCategorizationModelAsync(string model, CancellationToken ct)
    {
        CategorizationModel = model;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="ISecretStore"/> for view-model tests.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string?> GetSecretAsync(string name, CancellationToken ct) =>
        Task.FromResult(_secrets.TryGetValue(name, out var value) ? value : null);

    public Task SetSecretAsync(string name, string value, CancellationToken ct)
    {
        _secrets[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string name, CancellationToken ct)
    {
        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IAiUsageLedger"/> returning a fixed month-to-date spend.</summary>
internal sealed class FakeAiUsageLedger : IAiUsageLedger
{
    public decimal CurrentMonthSpendPln { get; set; }

    public Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct) => Task.CompletedTask;

    public Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct) =>
        Task.FromResult(CurrentMonthSpendPln);

    public Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AiSpendByPurpose>>([]);
}
