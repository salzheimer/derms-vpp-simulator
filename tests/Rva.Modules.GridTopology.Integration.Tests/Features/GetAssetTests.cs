using System.Net;
using System.Text.Json;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// C-01, C-01b, C-01c, C-02, C-11, C-20, C-21, C-22, C-23: <see cref="IGridTopologyQueries.GetAssetAsync"/> on
/// fixtures A-D, and <c>GET /api/topology/assets/{code}</c>.
/// </summary>
public class GetAssetTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fixture_A_reads_as_one_inverter_capped_PV_plus_storage_asset()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");

        var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        Assert.Equal(new KilovoltAmperes(7.6), asset.RatedKva);   // below the 20 kW component total (10 + 10)
        Assert.Equal(AssetMakeups.PvStorage, asset.Makeup);
        Assert.Equal(new KilowattHours(20), asset.EnergyCapacityKwh);
        Assert.Equal("FixtureCo", asset.ControlManufacturer);
    }

    [Fact]
    public async Task Every_snapshot_field_of_fixture_A_has_its_stored_value()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");
        var validFrom = await context.Database.ScalarAsync<DateTime>(
            "SELECT lower(sys_period) FROM topology.assets WHERE asset_code = 'FXA-ASSET'", Token);

        var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        var premise = new GridNodeRef(await context.GridNodeIdAsync("premises", "FXA-PREM"), GridNodeLevels.Premise, "FXA-PREM");
        var expected = new AssetSnapshot(
            id, "FXA-ASSET", premise,
            new SubstationId(await context.Database.ScalarAsync<long>("SELECT substation_id FROM topology.substations WHERE substation_code = 'FXA-SUB'", Token)),
            "FXA-SUB", new InterconnectionTypeCode("btm"),
            new Kilowatts(-7.6), new Kilowatts(7.6), new KilovoltAmperes(7.6), null,
            new SiteSnapshot(premise, "residential", new KilovoltAmperes(48), null, CanIsland: true),
            null,
            AssetMakeups.PvStorage, new KilowattHours(20), "FixtureCo", "commissioned", "active",
            new DateTimeOffset(DateTime.SpecifyKind(validFrom, DateTimeKind.Utc)));
        Assert.Equal(expected, asset);
    }

    [Fact]
    public async Task Every_snapshot_field_of_a_fixture_D_microgrid_member_has_its_stored_value()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-HOME-01-BESS");

        var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        var premise = new GridNodeRef(await context.GridNodeIdAsync("premises", "FXD-HOME-01"), GridNodeLevels.Premise, "FXD-HOME-01");
        Assert.Equal(premise, asset.Connection);
        Assert.Equal("FXD-SUB", asset.SubstationCode);
        Assert.Equal(new SiteSnapshot(premise, "residential", new KilovoltAmperes(48), null, CanIsland: false), asset.Site);
        Assert.Equal(new MicrogridSnapshot(
                new GridNodeRef(await context.GridNodeIdAsync("microgrids", "FXD-MG"), GridNodeLevels.Microgrid, "FXD-MG"),
                new Kilowatts(1000), new Kilowatts(500), CanIsland: true),
            asset.Microgrid);
        Assert.Equal("VendorA", asset.ControlManufacturer);
        // Fixture D's homes record no components: makeup and energy are unknown, not zero.
        Assert.Null(asset.Makeup);
        Assert.Null(asset.EnergyCapacityKwh);
    }

    [Fact]
    public async Task A_feeder_connected_battery_has_no_site_and_its_components_give_its_makeup()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-COMM-BESS");

        var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        Assert.Equal((GridNodeLevels.Feeder, "FXD-FDR"), (asset.Connection.Level, asset.Connection.Code));
        Assert.Equal(new InterconnectionTypeCode("ftm"), asset.InterconnectionType);
        Assert.Null(asset.Site);
        Assert.Equal("FXD-MG", asset.Microgrid?.Microgrid.Code);
        Assert.Equal(AssetMakeups.Storage, asset.Makeup);
        Assert.Equal(new KilowattHours(2000), asset.EnergyCapacityKwh);
    }

    [Fact]
    public async Task An_asset_without_a_control_profile_has_no_control_manufacturer()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-SCHOOL-PV");

        var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        Assert.Null(asset.ControlManufacturer);
    }

    [Fact]
    public async Task Customer_segment_is_independent_of_equipment_size()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        foreach (var code in new[] { "FXC-BESS-1", "FXC-BESS-2", "FXC-BESS-3" })
        {
            var id = await context.AssetIdAsync(code);
            var asset = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;
            Assert.Equal("residential", asset.Site?.CustomerSegment);
        }
    }

    [Fact]
    public async Task Reading_as_of_an_earlier_time_returns_the_version_valid_then()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");
        var before = await context.NowAsync();
        await context.ExecuteAsync("UPDATE topology.assets SET rated_p_max_kw = 7.0 WHERE asset_code = 'FXA-ASSET'");
        var oldValidFrom = await context.Database.ScalarAsync<DateTime>(
            "SELECT lower(sys_period) FROM topology.assets_history WHERE asset_code = 'FXA-ASSET'", Token);
        var newValidFrom = await context.Database.ScalarAsync<DateTime>(
            "SELECT lower(sys_period) FROM topology.assets WHERE asset_code = 'FXA-ASSET'", Token);

        var then = (await context.QueryAsync(q => q.GetAssetAsync(id, before, Token))).Value;
        var now = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;

        Assert.Equal(new Kilowatts(7.6), then.RatedPMaxKw);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(oldValidFrom, DateTimeKind.Utc)), then.ValidFrom);
        Assert.Equal(new Kilowatts(7.0), now.RatedPMaxKw);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(newValidFrom, DateTimeKind.Utc)), now.ValidFrom);
        Assert.True(now.ValidFrom > then.ValidFrom);
    }

    [Fact]
    public async Task A_decommissioned_asset_is_still_readable_now_and_as_of()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");
        await context.ExecuteAsync("""
            UPDATE topology.assets
            SET participation_state_id = topology.participation_state_id('opted_out'),
                asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'),
                decommissioned_date = DATE '2026-10-01'
            WHERE asset_code = 'FXA-ASSET'
            """);
        var after = await context.NowAsync();

        var now = (await context.QueryAsync(q => q.GetAssetAsync(id, null, Token))).Value;
        var asOf = (await context.QueryAsync(q => q.GetAssetAsync(id, after, Token))).Value;

        Assert.Equal(("decommissioned", "opted_out"), (now.LifecycleState, now.ParticipationState));
        Assert.Equal(now, asOf);
    }

    [Fact]
    public async Task An_unknown_asset_is_not_found()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetAssetAsync(new AssetId(987654), null, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.AssetNotFound), (result.Error.Kind, result.Error.Code));
    }

    [Fact]
    public async Task An_asset_read_as_of_a_time_before_it_existed_is_not_found()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var before = await context.NowAsync();
        await context.ExecuteAsync("""
            INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
            SELECT 'FXA-NEW', premise_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'btm'), -5, 5, 5
            FROM topology.premises WHERE premise_code = 'FXA-PREM'
            """);
        var id = await context.AssetIdAsync("FXA-NEW");

        var then = await context.QueryAsync(q => q.GetAssetAsync(id, before, Token));
        var now = await context.QueryAsync(q => q.GetAssetAsync(id, null, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.AssetNotFound), (then.Error.Kind, then.Error.Code));
        Assert.Equal("planned", now.Value.LifecycleState);
    }

    [Fact]
    public async Task A_future_as_of_is_a_validation_error()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");

        var result = await context.QueryAsync(q => q.GetAssetAsync(id, DateTimeOffset.UtcNow.AddMinutes(5), Token));

        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.AsOfInFuture), (result.Error.Kind, result.Error.Code));
    }

    // ------------------------------------------------------------------ endpoint

    [Fact]
    public async Task Endpoint_returns_the_asset_by_code_as_json()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/assets/FXA-ASSET", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = json.RootElement;
        Assert.Equal("FXA-ASSET", root.GetProperty("code").GetString());
        Assert.Equal(7.6, root.GetProperty("ratedKva").GetDouble());
        Assert.Equal("premise", root.GetProperty("connection").GetProperty("level").GetString());
        Assert.Equal("FXA-SUB", root.GetProperty("substationCode").GetString());
        Assert.Equal("residential", root.GetProperty("site").GetProperty("customerSegment").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("microgrid").ValueKind);
        Assert.Equal(AssetMakeups.PvStorage, root.GetProperty("makeup").GetString());
        Assert.Equal(20, root.GetProperty("energyCapacityKwh").GetDouble());
        Assert.Equal("FixtureCo", root.GetProperty("controlManufacturer").GetString());
    }

    [Fact]
    public async Task Endpoint_reads_as_of_a_time_with_a_utc_offset()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var before = await database.NowAsync(Token);
        await database.ExecuteAsync("UPDATE topology.assets SET rated_p_max_kw = 7.0 WHERE asset_code = 'FXA-ASSET'", Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        // The same instant written with a negative offset; '+' would need %2B in a query string.
        var asOf = Uri.EscapeDataString(before.ToOffset(TimeSpan.FromHours(-8)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture));
        var response = await client.GetAsync($"/api/topology/assets/FXA-ASSET?asOf={asOf}", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(7.6, json.RootElement.GetProperty("ratedPMaxKw").GetDouble());
    }

    [Fact]
    public async Task Endpoint_answers_404_for_an_unknown_asset()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/assets/NO-SUCH-ASSET", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.NotFound, GridTopologyErrors.AssetNotFound);
    }

    [Fact]
    public async Task Endpoint_answers_400_for_a_future_as_of()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync($"/api/topology/assets/FXA-ASSET?asOf={DateTimeOffset.UtcNow.AddDays(1):yyyy-MM-dd'T'HH:mm:ss'Z'}", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.AsOfInFuture);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00")]          // no UTC offset: ambiguous
    [InlineData("2026-01-01")]                   // a date, not a time
    [InlineData("yesterday")]
    [InlineData("2026-13-01T00:00:00Z")]         // no 13th month
    [InlineData("2026-01-01T00:00:00 08:00")]    // an unencoded '+' arrives as a space
    public async Task Endpoint_answers_400_for_a_malformed_as_of(string asOf)
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync($"/api/topology/assets/FXA-ASSET?asOf={Uri.EscapeDataString(asOf)}", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.AsOfInvalid);
    }
}
