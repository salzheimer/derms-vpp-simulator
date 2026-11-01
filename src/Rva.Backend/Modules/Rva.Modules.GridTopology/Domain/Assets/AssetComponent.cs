using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Domain.Assets;

/// <summary>What an asset is made of, for simulation; never dispatched directly (<c>topology.asset_components</c>).</summary>
internal sealed class AssetComponent
{
    private AssetComponent() { }

    internal AssetComponent(int technologyTypeId, int unitCount, Kilowatts? ratedKw, KilowattHours? ratedKwh, int? hardwareProfileId = null)
    {
        TechnologyTypeId = technologyTypeId;
        UnitCount = unitCount;
        RatedKw = ratedKw;
        RatedKwh = ratedKwh;
        HardwareProfileId = hardwareProfileId;
    }

    public long Id { get; private set; }
    public AssetId AssetId { get; private set; }
    public int TechnologyTypeId { get; private set; }

    /// <summary>Equipment model, of the component's technology; null = generic equipment.</summary>
    public int? HardwareProfileId { get; private set; }

    public int UnitCount { get; private set; }
    public Kilowatts? RatedKw { get; private set; }
    public KilowattHours? RatedKwh { get; private set; }
}
