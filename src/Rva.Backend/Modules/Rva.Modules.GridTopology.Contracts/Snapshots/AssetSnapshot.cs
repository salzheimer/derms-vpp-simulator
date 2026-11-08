using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>
/// One asset as it was at a point in time: where it connects, its ratings at that point, the site and microgrid limits
/// that apply to it, what it is made of, and its lifecycle and participation state. Ratings follow the sign
/// convention: positive = discharging or export, negative = charging or import.
/// </summary>
/// <param name="Connection">The node the asset connects to: a premise, transformer, feeder, or substation.</param>
/// <param name="SubstationId">The substation at the top of the asset's path.</param>
/// <param name="RatedPMinKw">Minimum real power at the connection point; negative = charging or import.</param>
/// <param name="RatedPMaxKw">Maximum real power at the connection point; positive = discharging or export.</param>
/// <param name="ExportLimitKw">Per-system export limit; null = no limit beyond <paramref name="RatedPMaxKw"/>.</param>
/// <param name="Site">The premise the asset is behind; null for an asset connected above the meter.</param>
/// <param name="Microgrid">The utility microgrid the asset is a member of; null = not a member.</param>
/// <param name="Makeup">
/// One of <see cref="AssetMakeups"/>, derived from the components; null when the asset has no components recorded.
/// </param>
/// <param name="EnergyCapacityKwh">
/// Total usable energy of the components that record one; null when none does (ex. PV only).
/// </param>
/// <param name="ControlManufacturer">Manufacturer of the control equipment; null when no control profile is recorded.</param>
/// <param name="LifecycleState">planned, installed, commissioned, or decommissioned.</param>
/// <param name="ParticipationState">not_enrolled, active, suspended, or opted_out.</param>
/// <param name="ValidFrom">When this version of the asset row took effect.</param>
public sealed record AssetSnapshot(
    AssetId Id,
    string Code,
    GridNodeRef Connection,
    SubstationId SubstationId,
    string SubstationCode,
    InterconnectionTypeCode InterconnectionType,
    Kilowatts RatedPMinKw,
    Kilowatts RatedPMaxKw,
    KilovoltAmperes RatedKva,
    Kilowatts? ExportLimitKw,
    SiteSnapshot? Site,
    MicrogridSnapshot? Microgrid,
    string? Makeup,
    KilowattHours? EnergyCapacityKwh,
    string? ControlManufacturer,
    string LifecycleState,
    string ParticipationState,
    DateTimeOffset ValidFrom);

/// <summary>
/// The customer site (premise) an asset is behind, with its site limits. A null limit means no limit is recorded at
/// the site, which is different from 0 (D20).
/// </summary>
/// <param name="CustomerSegment">The premise's service class, ex. residential; independent of equipment size.</param>
/// <param name="ServiceCapacityKva">Site import limit; null = not recorded.</param>
/// <param name="ExportLimitKw">Site export limit; 0 = non-export, null = not recorded.</param>
/// <param name="CanIsland">Every asset at the premise can island together (behind-the-meter microgrid).</param>
public sealed record SiteSnapshot(
    GridNodeRef Premise,
    string CustomerSegment,
    KilovoltAmperes? ServiceCapacityKva,
    Kilowatts? ExportLimitKw,
    bool CanIsland);

/// <summary>
/// A utility microgrid with its limits at the point of common coupling. A null limit means no limit is recorded, which
/// is different from 0 (D20).
/// </summary>
/// <param name="ImportLimitKw">Import limit at the PCC; null = not recorded.</param>
/// <param name="ExportLimitKw">Export limit at the PCC; 0 = non-export, null = not recorded.</param>
public sealed record MicrogridSnapshot(
    GridNodeRef Microgrid,
    Kilowatts? ImportLimitKw,
    Kilowatts? ExportLimitKw,
    bool CanIsland);

/// <summary>What an asset is made of, derived from the technologies of its components.</summary>
public static class AssetMakeups
{
    /// <summary>Solar PV components only.</summary>
    public const string Pv = "pv";

    /// <summary>Battery storage components only.</summary>
    public const string Storage = "storage";

    /// <summary>Solar PV and battery storage components, and nothing else.</summary>
    public const string PvStorage = "pv_storage";

    /// <summary>Any other combination, ex. an EV charger or wind.</summary>
    public const string Other = "other";
}
