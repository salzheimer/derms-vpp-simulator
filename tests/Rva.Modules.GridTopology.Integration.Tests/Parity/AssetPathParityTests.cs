using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Integration.Tests.Parity;

/// <summary>
/// C-10: for every asset in fixtures A-D, now and as of a time before a set of topology edits, the C# asset and
/// constraint path equal the pgTAP reference query <c>pg_temp.asset_paths</c>, including every grid node id.
/// </summary>
public class AssetPathParityTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csharp_paths_equal_the_reference_query_now_and_before_a_set_of_edits()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var before = await context.NowAsync();
        // Edits that change paths and limits, so "before" and "now" really differ: a reparenting (GT-09), a premise
        // moved to primary service, an unrated feeder, and changed site, asset, and microgrid limits.
        await context.ExecuteAsync("""
            UPDATE topology.premises
            SET neighborhood_transformer_id = (SELECT neighborhood_transformer_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-XFMR-2')
            WHERE premise_code = 'FXD-HOME-05';
            UPDATE topology.premises
            SET neighborhood_transformer_id = NULL, feeder_id = (SELECT feeder_id FROM topology.feeders WHERE feeder_code = 'FXB-FDR')
            WHERE premise_code = 'FXB-PREM';
            UPDATE topology.feeders SET rated_capacity_kva = NULL WHERE feeder_code = 'FXA-FDR';
            UPDATE topology.premises SET export_limit_kw = 4 WHERE premise_code = 'FXC-PREM';
            UPDATE topology.assets SET export_limit_kw = 3 WHERE asset_code = 'FXA-ASSET';
            UPDATE topology.microgrids SET export_limit_kw = 0 WHERE microgrid_code = 'FXD-MG';
            """);

        foreach (var at in new DateTimeOffset?[] { before, null })
        {
            var reference = await ReferenceQueries.AssetPathsAsync(context.Database, at ?? await context.NowAsync(), Token);
            Assert.Equal(30, reference.Count);   // A 1, B 2, C 3, D 24

            foreach (var expected in reference)
            {
                var id = new AssetId(expected.AssetId);
                var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, at, Token))).Value;
                var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, at, Token))).Value;
                Assert.Equal(expected, FromCsharp(asset, path));
            }
        }
    }

    // The C# results in the reference query's shape: one column per level, null for the levels the asset sits above.
    private static ReferenceQueries.AssetPath FromCsharp(AssetSnapshot asset, ConstraintPathSnapshot path)
    {
        ConstraintNodeSnapshot? At(string level) => path.Nodes.SingleOrDefault(n => n.Node.Level == level);
        var premise = At(GridNodeLevels.Premise);
        var transformer = At(GridNodeLevels.NeighborhoodTransformer);
        var feeder = At(GridNodeLevels.Feeder);
        var substation = At(GridNodeLevels.Substation);

        Assert.Equal(asset.Code, path.AssetCode);
        Assert.Equal(asset.Site, path.Site);
        Assert.Equal(asset.Microgrid, path.Microgrid);
        Assert.Equal(asset.Connection, path.Nodes[0].Node);
        Assert.Equal(asset.SubstationCode, substation?.Node.Code);

        return new ReferenceQueries.AssetPath(
            asset.Code, asset.Id.Value, asset.LifecycleState, asset.ParticipationState,
            asset.RatedKva.Value, asset.RatedPMaxKw.Value, asset.ExportLimitKw?.Value, asset.InterconnectionType.Value,
            premise?.Node.Code, asset.Site?.ExportLimitKw?.Value,
            transformer?.Node.Code, transformer?.RatedCapacityKva?.Value,
            feeder?.Node.Code, feeder?.RatedCapacityKva?.Value,
            substation?.Node.Code, substation?.RatedCapacityKva?.Value,
            asset.Microgrid?.Microgrid.Code, asset.Microgrid?.ExportLimitKw?.Value,
            substation?.Node.GridNodeId.Value, feeder?.Node.GridNodeId.Value, asset.Microgrid?.Microgrid.GridNodeId.Value,
            transformer?.Node.GridNodeId.Value, premise?.Node.GridNodeId.Value);
    }
}
