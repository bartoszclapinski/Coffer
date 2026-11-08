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

    public string AnalysisProvider { get; set; } = AiDefaults.ClaudeProvider;

    public string AnalysisModel { get; set; } = AiDefaults.AnalysisModel;

    public Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct) => Task.FromResult(MonthlyCapPln);

    public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct)
    {
        MonthlyCapPln = capPln;
        return Task.CompletedTask;
    }

    public int SetRouteCalls { get; private set; }

    public Task<AiRoute> GetRouteAsync(AiRole role, CancellationToken ct) =>
        Task.FromResult(role == AiRole.Categorization
            ? new AiRoute(ActiveProvider, CategorizationModel)
            : new AiRoute(AnalysisProvider, AnalysisModel));

    public Task SetRouteAsync(AiRole role, AiRoute route, CancellationToken ct)
    {
        SetRouteCalls++;
        if (role == AiRole.Categorization)
        {
            ActiveProvider = route.Provider;
            CategorizationModel = route.Model;
        }
        else
        {
            AnalysisProvider = route.Provider;
            AnalysisModel = route.Model;
        }

        return Task.CompletedTask;
    }

    public bool AiFallbackParsingEnabled { get; set; } = AiDefaults.AiFallbackParsingEnabled;

    public string? OwnerIdentityNames { get; set; }

    public Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct) =>
        Task.FromResult(AiFallbackParsingEnabled);

    public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct)
    {
        AiFallbackParsingEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct) =>
        Task.FromResult(OwnerIdentityNames);

    public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct)
    {
        OwnerIdentityNames = names;
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
