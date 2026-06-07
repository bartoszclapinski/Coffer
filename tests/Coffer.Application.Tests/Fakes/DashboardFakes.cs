using Coffer.Core.Dashboard;

namespace Coffer.Application.Tests.Fakes;

/// <summary>Returns a pre-set <see cref="DashboardSnapshot"/> (or throws), recording each call.</summary>
internal sealed class FakeDashboardQuery : IDashboardQuery
{
    private readonly DashboardSnapshot _snapshot;

    public FakeDashboardQuery(DashboardSnapshot? snapshot = null) =>
        _snapshot = snapshot ?? Empty();

    public int Calls { get; private set; }

    public DashboardFilter? LastFilter { get; private set; }

    public Exception? Throw { get; set; }

    public Task<DashboardSnapshot> GetSnapshotAsync(DashboardFilter filter, CancellationToken ct)
    {
        Calls++;
        LastFilter = filter;

        return Throw is not null
            ? Task.FromException<DashboardSnapshot>(Throw)
            : Task.FromResult(_snapshot);
    }

    private static DashboardSnapshot Empty() =>
        new(
            MonthlySummary.Empty(new DateOnly(2026, 6, 1), "PLN"),
            [],
            [],
            [],
            [],
            HasData: false);
}
