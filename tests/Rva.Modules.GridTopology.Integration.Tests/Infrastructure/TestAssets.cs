using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

internal static class TestAssets
{
    /// <summary>A planned, not-enrolled 5 kW battery at fixture A's premise, not yet added to a context.</summary>
    public static async Task<Asset> PlannedBatteryAtFixtureAAsync(IServiceProvider services, string code, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<GridTopologyDbContext>();
        var catalog = services.GetRequiredService<LookupCatalog>();
        var premise = await db.Premises.SingleAsync(p => p.Code == FixtureCodes.FixtureAPremise, cancellationToken);

        return new Asset(
            code,
            new AssetConnection(PremiseId: premise.Id),
            interconnectionTypeId: await catalog.IdOfAsync(LookupTable.InterconnectionTypes, "btm", cancellationToken),
            lifecycleStateId: await catalog.IdOfAsync(LookupTable.AssetLifecycleStates, "planned", cancellationToken),
            participationStateId: await catalog.IdOfAsync(LookupTable.ParticipationStates, "not_enrolled", cancellationToken),
            ratedPMin: new Kilowatts(-5),
            ratedPMax: new Kilowatts(5),
            ratedKva: new KilovoltAmperes(5),
            components: [new AssetComponent(await catalog.IdOfAsync(LookupTable.TechnologyTypes, "bess", cancellationToken), 1, new Kilowatts(5), new KilowattHours(10))]);
    }
}
