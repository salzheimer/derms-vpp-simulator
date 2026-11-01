namespace Rva.Modules.GridTopology.Domain.Catalog;

/// <summary>A manufacturer equipment model with per-unit nameplate specs (<c>topology.hardware_profiles</c>).</summary>
internal sealed class HardwareProfile
{
    private HardwareProfile() { }

    public int Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Manufacturer { get; private set; } = "";
    public string Model { get; private set; } = "";

    /// <summary>Energy technology; null for conversion or control equipment, which components cannot use.</summary>
    public int? TechnologyTypeId { get; private set; }

    public double? RatedKw { get; private set; }
    public double? RatedKva { get; private set; }
    public double? RatedKwh { get; private set; }
    public double? RoundTripEfficiency { get; private set; }
}
