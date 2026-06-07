namespace Coffer.Core.Dashboard;

/// <summary>
/// Scopes the dashboard read model. <see cref="Currency"/> is the single display
/// currency the figures are computed in (PLN by default — multi-currency split is a
/// later phase); <see cref="AccountId"/> optionally narrows to one account (null =
/// all accounts combined). <see cref="AsOf"/> anchors the "current month" and the
/// trailing windows so the result is deterministic and testable (null = today, UTC).
/// </summary>
public sealed record DashboardFilter(
    string Currency = "PLN",
    Guid? AccountId = null,
    DateOnly? AsOf = null);
