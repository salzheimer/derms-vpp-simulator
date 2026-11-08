using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Tests.Domain;

/// <summary>Makeup and energy capacity derived from an asset's components (Step 1: "derived from its components").</summary>
public class AssetMakeupTests
{
    [Theory]
    [InlineData(new[] { "solar_pv" }, AssetMakeups.Pv)]
    [InlineData(new[] { "bess" }, AssetMakeups.Storage)]
    [InlineData(new[] { "bess", "bess" }, AssetMakeups.Storage)]
    [InlineData(new[] { "bess", "solar_pv" }, AssetMakeups.PvStorage)]
    [InlineData(new[] { "evse" }, AssetMakeups.Other)]
    [InlineData(new[] { "solar_pv", "bess", "wind" }, AssetMakeups.Other)]
    public void Makeup_follows_the_component_technologies(string[] technologies, string expected) =>
        Assert.Equal(expected, AssetMakeup.Derive(technologies));

    [Fact]
    public void An_asset_without_components_has_no_makeup() =>
        Assert.Null(AssetMakeup.Derive([]));

    [Fact]
    public void Energy_capacity_totals_the_components_that_record_one()
    {
        AssetComponent[] components =
        [
            new(1, 2, new Kilowatts(10), new KilowattHours(20)),   // already a total for its two units
            new(2, 25, new Kilowatts(10), null),                   // PV records no energy
            new(1, 1, new Kilowatts(5), new KilowattHours(13.5)),
        ];

        Assert.Equal(33.5, AssetMakeup.EnergyCapacityKwh(components));
    }

    [Fact]
    public void Energy_capacity_is_unknown_when_no_component_records_one()
    {
        Assert.Null(AssetMakeup.EnergyCapacityKwh([new AssetComponent(2, 1, new Kilowatts(100), null)]));
        Assert.Null(AssetMakeup.EnergyCapacityKwh([]));
    }
}
