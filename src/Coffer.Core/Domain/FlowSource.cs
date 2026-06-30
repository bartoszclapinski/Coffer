namespace Coffer.Core.Domain;

/// <summary>
/// How a <see cref="RecurringFlow"/> came to exist: proposed by the detector from transaction
/// history, or entered by the owner. Detected flows are suggestions until the owner confirms them.
/// </summary>
public enum FlowSource
{
    Detected,
    Manual,
}
