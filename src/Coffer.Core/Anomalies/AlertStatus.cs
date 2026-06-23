namespace Coffer.Core.Anomalies;

/// <summary>
/// Lifecycle of an <see cref="Domain.Alert"/>. A re-run never resurrects a
/// <see cref="Dismissed"/> alert (the signature is remembered), so "to normalne"
/// sticks across imports.
/// </summary>
public enum AlertStatus
{
    /// <summary>Surfaced to the user, awaiting a decision.</summary>
    New,

    /// <summary>The user accepted it as real ("zajmę się tym").</summary>
    Acknowledged,

    /// <summary>The user marked it normal ("to normalne"); stays dismissed on re-run.</summary>
    Dismissed,
}
