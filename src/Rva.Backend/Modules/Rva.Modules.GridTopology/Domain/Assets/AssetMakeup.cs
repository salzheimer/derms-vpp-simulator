using Rva.Modules.GridTopology.Contracts.Snapshots;

namespace Rva.Modules.GridTopology.Domain.Assets;

/// <summary>What an asset is made of (<see cref="AssetMakeups"/>), derived from its components' technology codes.</summary>
internal static class AssetMakeup
{
    private const string SolarPv = "solar_pv";
    private const string Battery = "bess";

    /// <summary>Null when the asset has no components recorded; otherwise one of <see cref="AssetMakeups"/>.</summary>
    public static string? Derive(IEnumerable<string> componentTechnologies)
    {
        var technologies = componentTechnologies.ToHashSet(StringComparer.Ordinal);
        if (technologies.Count == 0)
            return null;

        var pv = technologies.Remove(SolarPv);
        var storage = technologies.Remove(Battery);
        return technologies.Count > 0 ? AssetMakeups.Other
            : pv && storage ? AssetMakeups.PvStorage
            : pv ? AssetMakeups.Pv
            : AssetMakeups.Storage;
    }

    /// <summary>The total of the recorded energy capacities, or null when no component records one.</summary>
    public static double? EnergyCapacityKwh(IEnumerable<AssetComponent> components)
    {
        double? total = null;
        foreach (var component in components)
        {
            if (component.RatedKwh is { } kwh)
                total = (total ?? 0) + kwh.Value;
        }
        return total;
    }
}
