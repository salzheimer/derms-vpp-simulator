using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>Column conversions for the shared-kernel identifiers and units, registered as conventions.</summary>
internal static class ValueConverters
{
    internal sealed class AssetIdConverter() : ValueConverter<AssetId, long>(v => v.Value, v => new AssetId(v));
    internal sealed class GridNodeIdConverter() : ValueConverter<GridNodeId, long>(v => v.Value, v => new GridNodeId(v));
    internal sealed class SubstationIdConverter() : ValueConverter<SubstationId, long>(v => v.Value, v => new SubstationId(v));
    internal sealed class FeederIdConverter() : ValueConverter<FeederId, long>(v => v.Value, v => new FeederId(v));
    internal sealed class TransformerIdConverter() : ValueConverter<TransformerId, long>(v => v.Value, v => new TransformerId(v));
    internal sealed class PremiseIdConverter() : ValueConverter<PremiseId, long>(v => v.Value, v => new PremiseId(v));
    internal sealed class MicrogridIdConverter() : ValueConverter<MicrogridId, long>(v => v.Value, v => new MicrogridId(v));
    internal sealed class KilowattsConverter() : ValueConverter<Kilowatts, double>(v => v.Value, v => new Kilowatts(v));
    internal sealed class KilowattHoursConverter() : ValueConverter<KilowattHours, double>(v => v.Value, v => new KilowattHours(v));
    internal sealed class KilovoltAmperesConverter() : ValueConverter<KilovoltAmperes, double>(v => v.Value, v => new KilovoltAmperes(v));
}
