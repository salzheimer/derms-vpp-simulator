using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>C-13, C-25: <see cref="IGridTopologyQueries.ResolveNodeAsync"/> on fixtures A-D (supports SIM-01).</summary>
public class ResolveNodeTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(GridNodeLevels.Substation, "substations", "FXD-SUB")]
    [InlineData(GridNodeLevels.Feeder, "feeders", "FXD-FDR")]
    [InlineData(GridNodeLevels.NeighborhoodTransformer, "neighborhood_transformers", "FXD-XFMR-1")]
    [InlineData(GridNodeLevels.Premise, "premises", "FXD-SCHOOL")]
    [InlineData(GridNodeLevels.Microgrid, "microgrids", "FXD-MG")]
    public async Task A_level_and_code_resolve_to_the_nodes_grid_node_id(string level, string table, string code)
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var node = (await context.QueryAsync(q => q.ResolveNodeAsync(level, code, null, Token))).Value;

        Assert.Equal(new GridNodeRef(await context.GridNodeIdAsync(table, code), level, code), node);
    }

    [Fact]
    public async Task A_node_resolves_as_of_a_time_only_once_it_exists()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var before = await context.NowAsync();
        await context.ExecuteAsync("INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXE-SUB', 10000)");
        var after = await context.NowAsync();

        var then = await context.QueryAsync(q => q.ResolveNodeAsync(GridNodeLevels.Substation, "FXE-SUB", before, Token));
        var later = await context.QueryAsync(q => q.ResolveNodeAsync(GridNodeLevels.Substation, "FXE-SUB", after, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.NodeNotFound), (then.Error.Kind, then.Error.Code));
        Assert.Equal(await context.GridNodeIdAsync("substations", "FXE-SUB"), later.Value.GridNodeId);
    }

    [Theory]
    [InlineData(GridNodeLevels.Feeder, "FXD-SUB")]                  // a substation code
    [InlineData(GridNodeLevels.Premise, "FXD-XFMR-1")]              // a transformer code
    [InlineData(GridNodeLevels.Microgrid, "FXD-FDR")]               // the microgrid's feeder
    public async Task A_code_that_exists_only_at_another_level_is_not_found(string level, string code)
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.ResolveNodeAsync(level, code, null, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.NodeNotFound), (result.Error.Kind, result.Error.Code));
    }

    [Theory]
    [InlineData("transformer", "FXD-XFMR-1")]   // not a level code
    [InlineData("Substation", "FXD-SUB")]       // levels compare ordinally
    [InlineData(GridNodeLevels.Substation, " ")]
    public async Task An_unknown_level_or_blank_code_is_a_validation_error(string level, string code)
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.ResolveNodeAsync(level, code, null, Token));

        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.NodeQueryInvalid), (result.Error.Kind, result.Error.Code));
    }
}
