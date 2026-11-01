using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Domain.Assets;

/// <summary>
/// A grid-facing controllable resource: everything behind one control interface at one connection point, with one
/// rating at that point (<c>topology.assets</c>). Exactly one of the four connection columns is set. The registration,
/// lifecycle, and participation rules are added with the asset write features (plan p1-s01e); the database enforces
/// its own constraints as a backstop.
/// </summary>
internal sealed class Asset
{
    private readonly List<AssetComponent> components = [];

    private Asset() { }

    internal Asset(
        string code,
        AssetConnection connection,
        int interconnectionTypeId,
        int lifecycleStateId,
        int participationStateId,
        Kilowatts ratedPMin,
        Kilowatts ratedPMax,
        KilovoltAmperes ratedKva,
        IEnumerable<AssetComponent> components,
        Kilowatts? exportLimit = null,
        int? controlHardwareProfileId = null,
        MicrogridId? microgridId = null)
    {
        Code = code;
        PremiseId = connection.PremiseId;
        TransformerId = connection.TransformerId;
        FeederId = connection.FeederId;
        SubstationId = connection.SubstationId;
        InterconnectionTypeId = interconnectionTypeId;
        LifecycleStateId = lifecycleStateId;
        ParticipationStateId = participationStateId;
        RatedPMin = ratedPMin;
        RatedPMax = ratedPMax;
        RatedKva = ratedKva;
        ExportLimit = exportLimit;
        ControlHardwareProfileId = controlHardwareProfileId;
        MicrogridId = microgridId;
        this.components.AddRange(components);
    }

    public AssetId Id { get; private set; }
    public string Code { get; private set; } = "";

    public PremiseId? PremiseId { get; private set; }
    public TransformerId? TransformerId { get; private set; }
    public FeederId? FeederId { get; private set; }
    public SubstationId? SubstationId { get; private set; }

    /// <summary>Utility-side microgrid membership; the connection point is one of the columns above.</summary>
    public MicrogridId? MicrogridId { get; private set; }

    public int InterconnectionTypeId { get; private set; }

    /// <summary>Controller commands go through; null = not recorded (D21).</summary>
    public int? ControlHardwareProfileId { get; private set; }

    public int LifecycleStateId { get; private set; }
    public int ParticipationStateId { get; private set; }
    public DateOnly? InServiceDate { get; private set; }
    public DateOnly? DecommissionedDate { get; private set; }

    /// <summary>Minimum real power at the connection point; negative = charging or import.</summary>
    public Kilowatts RatedPMin { get; private set; }

    /// <summary>Maximum real power at the connection point; positive = discharging or export.</summary>
    public Kilowatts RatedPMax { get; private set; }

    public KilovoltAmperes RatedKva { get; private set; }

    /// <summary>Per-system export limit; null = no limit beyond <see cref="RatedPMax"/>.</summary>
    public Kilowatts? ExportLimit { get; private set; }

    public IReadOnlyList<AssetComponent> Components => components;
}

/// <summary>Where an asset connects: exactly one level of the hierarchy.</summary>
internal sealed record AssetConnection(
    PremiseId? PremiseId = null,
    TransformerId? TransformerId = null,
    FeederId? FeederId = null,
    SubstationId? SubstationId = null);
