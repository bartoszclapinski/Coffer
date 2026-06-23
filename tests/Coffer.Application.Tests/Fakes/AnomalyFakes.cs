using Coffer.Core.Anomalies;

namespace Coffer.Application.Tests.Fakes;

public sealed class FakeDetectAnomaliesUseCase : IDetectAnomaliesUseCase
{
    public int Calls { get; private set; }

    public int Result { get; set; }

    public Exception? Throw { get; set; }

    public Task<int> RunAsync(CancellationToken ct)
    {
        Calls++;
        return Throw is null ? Task.FromResult(Result) : Task.FromException<int>(Throw);
    }
}

public sealed class FakeAlertsQuery : IAlertsQuery
{
    public List<AlertListItem> Items { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<AlertListItem>> GetActiveAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<AlertListItem>>(Items.ToList());
    }
}

public sealed class FakeAlertService : IAlertService
{
    public List<Guid> Acknowledged { get; } = [];

    public List<Guid> Dismissed { get; } = [];

    public Task AcknowledgeAsync(Guid alertId, CancellationToken ct)
    {
        Acknowledged.Add(alertId);
        return Task.CompletedTask;
    }

    public Task DismissAsync(Guid alertId, CancellationToken ct)
    {
        Dismissed.Add(alertId);
        return Task.CompletedTask;
    }
}
