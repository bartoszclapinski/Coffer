namespace Coffer.Core.Planning;

/// <summary>
/// Owner-configurable planning settings, persisted in the encrypted DB. The safety floor is the buffer
/// the owner never wants to dip below — it replaces the hard-coded <c>0</c> "tight" threshold in the
/// cash-flow projection and the affordability engine, so "tight" means "below your buffer", not "below
/// zero". Kept separate from <c>IAiSettings</c>: planning is not an AI concern. Sensible defaults
/// (<see cref="PlanningDefaults"/>) apply until the owner changes them.
/// </summary>
public interface IPlanningSettings
{
    Task<decimal> GetSafetyFloorPlnAsync(CancellationToken ct);

    Task SetSafetyFloorPlnAsync(decimal floorPln, CancellationToken ct);
}
