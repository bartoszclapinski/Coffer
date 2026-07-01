namespace Coffer.Core.Planning;

/// <summary>Defaults for owner-configurable planning settings (see <see cref="IPlanningSettings"/>).</summary>
public static class PlanningDefaults
{
    /// <summary>
    /// Default safety floor in PLN. Zero preserves the pre-18-B behaviour ("tight" = below zero) until
    /// the owner sets a personal buffer in Ustawienia.
    /// </summary>
    public const decimal SafetyFloorPln = 0m;
}
