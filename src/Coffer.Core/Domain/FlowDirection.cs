namespace Coffer.Core.Domain;

/// <summary>
/// Whether a <see cref="RecurringFlow"/> brings money in or takes it out. The flow's amount is a
/// positive magnitude; this direction carries the sign on the cash-flow timeline.
/// </summary>
public enum FlowDirection
{
    Inflow,
    Outflow,
}
