using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// C-08 and C-26 (cursor and page size; the listing is contract only, so these are contract-level Validation errors,
/// not 400s): <see cref="IGridTopologyQueries.GetAssetsBySubstationAsync"/> on fixtures A-D. Paging over the
/// development seed is in <see cref="Parity.DevTopologyTests"/>.
/// </summary>
public class AssetsBySubstationTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string FixtureDAssets = "SELECT string_agg(asset_code, ',' ORDER BY asset_id) FROM topology.assets WHERE asset_code LIKE 'FXD-%'";

    [Fact]
    public async Task Lists_every_asset_under_the_substation_at_all_four_connection_levels_and_none_from_elsewhere()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var before = await context.NowAsync();
        var fixtureDBefore = await context.Database.ScalarAsync<string>(FixtureDAssets, Token);
        await AddTransformerAndSubstationAssetsAsync(context);

        var page = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, null, 1000, Token))).Value;

        Assert.Equal((await context.Database.ScalarAsync<string>(FixtureDAssets, Token)).Split(','), page.Assets.Select(a => a.Code));
        Assert.Equal(
            new[] { GridNodeLevels.Feeder, GridNodeLevels.NeighborhoodTransformer, GridNodeLevels.Premise, GridNodeLevels.Substation },
            page.Assets.Select(a => a.Connection.Level).Distinct().Order(StringComparer.Ordinal));
        Assert.All(page.Assets, a => Assert.Equal("FXD-SUB", a.SubstationCode));
        Assert.Null(page.NextCursor);

        var then = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", before, null, 1000, Token))).Value;
        Assert.Equal(fixtureDBefore.Split(','), then.Assets.Select(a => a.Code));
        Assert.Equal(before, then.AsOf);
    }

    [Fact]
    public async Task Pages_follow_asset_id_order_and_a_cursor_continues_where_the_last_page_ended()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var first = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, null, 10, Token))).Value;
        var second = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, first.NextCursor, 10, Token))).Value;
        var third = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", first.AsOf, second.NextCursor, 10, Token))).Value;

        Assert.Equal((10, 10, 4), (first.Assets.Count, second.Assets.Count, third.Assets.Count));
        Assert.Null(third.NextCursor);
        Assert.Equal((first.AsOf, first.AsOf), (second.AsOf, third.AsOf));
        var ids = first.Assets.Concat(second.Assets).Concat(third.Assets).Select(a => a.Id.Value).ToList();
        Assert.Equal(ids.Order(), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task An_unknown_substation_is_not_found()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("NO-SUCH-SUB", null, null, 100, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.SubstationNotFound), (result.Error.Kind, result.Error.Code));
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("djE6MTox")]       // base64url of "v1:1:1": too few parts
    [InlineData("")]
    public async Task A_malformed_cursor_is_a_validation_error(string cursor)
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, cursor, 10, Token));

        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.PageCursorInvalid), (result.Error.Kind, result.Error.Code));
    }

    [Fact]
    public async Task A_cursor_from_another_substation_or_with_another_as_of_is_a_validation_error()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var first = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, null, 5, Token))).Value;

        var otherSubstation = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXA-SUB", null, first.NextCursor, 5, Token));
        var otherAsOf = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", first.AsOf.AddSeconds(-1), first.NextCursor, 5, Token));

        Assert.Equal(GridTopologyErrors.PageCursorInvalid, otherSubstation.Error.Code);
        Assert.Equal(GridTopologyErrors.PageCursorInvalid, otherAsOf.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IGridTopologyQueries.MaxAssetPageSize + 1)]
    public async Task A_page_size_out_of_range_is_a_validation_error(int pageSize)
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", null, null, pageSize, Token));

        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.PageSizeInvalid), (result.Error.Kind, result.Error.Code));
    }

    [Fact]
    public async Task A_future_as_of_is_a_validation_error()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetAssetsBySubstationAsync("FXD-SUB", DateTimeOffset.UtcNow.AddHours(1), null, 10, Token));

        Assert.Equal(GridTopologyErrors.AsOfInFuture, result.Error.Code);
    }

    /// <summary>FTM assets at fixture D's transformer and substation, so all four connection levels are present.</summary>
    internal static Task AddTransformerAndSubstationAssetsAsync(ReadContext context) => context.ExecuteAsync("""
        INSERT INTO topology.assets (asset_code, neighborhood_transformer_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        SELECT 'FXD-XFMR-2-BESS', neighborhood_transformer_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'ftm'), -50, 50, 50
        FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-XFMR-2';
        INSERT INTO topology.assets (asset_code, substation_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        SELECT 'FXD-SUB-BESS', substation_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'ftm'), -2000, 2000, 2000
        FROM topology.substations WHERE substation_code = 'FXD-SUB';
        """);
}
