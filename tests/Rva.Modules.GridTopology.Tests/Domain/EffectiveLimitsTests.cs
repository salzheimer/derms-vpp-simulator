using Rva.Modules.GridTopology.Domain.ConstraintPaths;

namespace Rva.Modules.GridTopology.Tests.Domain;

/// <summary>
/// The per-asset limits behind C-06, C-06b, C-06c (GT-04, GT-12, D20): the minimum along the path, null skipped,
/// 0 kept, never negative.
/// </summary>
public class EffectiveLimitsTests
{
    [Theory]
    [InlineData(7.6, null, null, null, 7.6)]       // nothing recorded: the asset's own capability
    [InlineData(11.5, null, 10.0, null, 10)]       // premise cap below p_max (GT-04)
    [InlineData(11.5, 20.0, 10.0, null, 10)]       // a per-system limit above the cap does not raise it (GT-12)
    [InlineData(11.5, 5.0, 10.0, null, 5)]         // a per-system limit below the cap binds
    [InlineData(100, null, 0.0, null, 0)]          // non-export premise: 0, not "no limit" (D20)
    [InlineData(500, null, null, 0.0, 0)]          // non-export microgrid PCC (GT-12)
    [InlineData(500, null, null, 400.0, 400)]
    [InlineData(0, null, null, null, 0)]           // an EV charger cannot export
    [InlineData(-2, null, null, null, 0)]          // an asset that must charge: still never negative
    public void Export_is_the_minimum_of_every_recorded_limit(double pMax, double? asset, double? site, double? microgrid, double expected) =>
        Assert.Equal(expected, EffectiveLimits.Export(pMax, asset, site, microgrid));

    [Theory]
    [InlineData(-7.6, 48.0, null, 7.6)]            // |p_min| below the service capacity
    [InlineData(-7.6, 5.0, null, 5)]               // the service capacity binds
    [InlineData(-500, null, 1000.0, 500)]
    [InlineData(-500, null, 0.0, 0)]               // a microgrid import limit of 0 (C-06b)
    [InlineData(-7.6, null, null, 7.6)]            // null limits skipped
    [InlineData(0, 48.0, null, 0)]                 // PV only cannot import
    [InlineData(2, 48.0, null, 0)]                 // an asset that must export: never negative
    public void Import_is_the_minimum_of_charging_power_service_capacity_and_microgrid_import(double pMin, double? site, double? microgrid, double expected) =>
        Assert.Equal(expected, EffectiveLimits.Import(pMin, site, microgrid));
}
