using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>C-09, C-16, C-24, C-26 (size limit), C-31: <c>GET /api/topology/substations/{code}/tree</c>.</summary>
public class SubstationTreeTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fixture_D_nests_feeders_transformers_premises_and_assets_with_the_fixtures_counts()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        using var tree = await GetTreeAsync(client, "FXD-SUB");

        var substation = tree.RootElement.GetProperty("substation");
        Assert.Equal("FXD-SUB", substation.GetProperty("code").GetString());
        var feeders = substation.GetProperty("feeders").EnumerateArray().ToList();
        Assert.Equal(new[] { "FXD-FDR", "FXD-FDR-2" }, feeders.Select(f => f.GetProperty("code").GetString()));

        var fdr = feeders[0];
        var transformers = fdr.GetProperty("transformers").EnumerateArray().ToList();
        Assert.Equal(5, transformers.Count);
        var xfmr1 = transformers.Single(x => x.GetProperty("code").GetString() == "FXD-XFMR-1");
        Assert.Equal(25, xfmr1.GetProperty("ratedCapacityKva").GetDouble());
        Assert.Equal(new[] { "FXD-HOME-01", "FXD-HOME-02", "FXD-HOME-03", "FXD-HOME-04", "FXD-HOME-05" },
            xfmr1.GetProperty("premises").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.All(xfmr1.GetProperty("premises").EnumerateArray(), p => Assert.Single(p.GetProperty("assets").EnumerateArray()));
        var school = transformers.Single(x => x.GetProperty("code").GetString() == "FXD-XFMR-SCH").GetProperty("premises")[0];
        Assert.Equal("commercial_industrial", school.GetProperty("customerSegment").GetString());
        Assert.Equal(2, school.GetProperty("assets").GetArrayLength());
        var feederAsset = Assert.Single(fdr.GetProperty("assets").EnumerateArray());
        Assert.Equal(("FXD-COMM-BESS", "ftm", "FXD-MG"),
            (feederAsset.GetProperty("code").GetString(), feederAsset.GetProperty("interconnectionType").GetString(), feederAsset.GetProperty("microgridCode").GetString()));

        // 2 feeders, 6 transformers, 22 premises, 24 assets; one microgrid with every FXD-FDR asset as a member.
        Assert.Equal(6, feeders.Sum(f => f.GetProperty("transformers").GetArrayLength()));
        Assert.Equal(22, feeders.SelectMany(f => f.GetProperty("transformers").EnumerateArray()).Sum(x => x.GetProperty("premises").GetArrayLength()));
        Assert.Equal(24, CountAssets(substation));
        var microgrid = Assert.Single(substation.GetProperty("microgrids").EnumerateArray());
        Assert.Equal(("FXD-MG", "FXD-FDR", 23), (microgrid.GetProperty("code").GetString(), microgrid.GetProperty("feederCode").GetString(), microgrid.GetProperty("memberCount").GetInt32()));
    }

    [Fact]
    public async Task Locations_round_trip_as_GeoJSON_in_WGS84_and_a_missing_location_is_null()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await database.ExecuteAsync("""
            UPDATE topology.substations SET location = ST_SetSRID(ST_MakePoint(-122.6765, 45.5231), 4326)::geography WHERE substation_code = 'FXD-SUB';
            UPDATE topology.feeders SET line_geometry = ST_SetSRID(ST_MakeLine(ST_MakePoint(-122.6765, 45.5231), ST_MakePoint(-122.6601, 45.5302)), 4326)
                WHERE feeder_code = 'FXD-FDR';
            UPDATE topology.neighborhood_transformers SET location = ST_SetSRID(ST_MakePoint(-122.6650, 45.5280), 4326)::geography
                WHERE neighborhood_transformer_code = 'FXD-XFMR-1';
            UPDATE topology.premises SET location = ST_SetSRID(ST_MakePoint(-122.66512, 45.52817), 4326)::geography WHERE premise_code = 'FXD-HOME-01';
            """, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        using var tree = await GetTreeAsync(client, "FXD-SUB");

        var substation = tree.RootElement.GetProperty("substation");
        AssertPoint(substation.GetProperty("location"), -122.6765, 45.5231);
        var fdr = substation.GetProperty("feeders")[0];
        var line = fdr.GetProperty("location");
        Assert.Equal("LineString", line.GetProperty("type").GetString());
        Assert.Equal(new[] { new[] { -122.6765, 45.5231 }, new[] { -122.6601, 45.5302 } },
            line.GetProperty("coordinates").EnumerateArray().Select(p => p.EnumerateArray().Select(c => c.GetDouble()).ToArray()));
        var xfmr1 = fdr.GetProperty("transformers").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "FXD-XFMR-1");
        AssertPoint(xfmr1.GetProperty("location"), -122.6650, 45.5280);
        var premises = xfmr1.GetProperty("premises").EnumerateArray().ToList();
        AssertPoint(premises[0].GetProperty("location"), -122.66512, 45.52817);
        Assert.Equal(JsonValueKind.Null, premises[1].GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, substation.GetProperty("feeders")[1].GetProperty("location").ValueKind);
    }

    [Fact]
    public async Task The_tree_as_of_an_earlier_time_shows_the_topology_then()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var before = await database.NowAsync(Token);
        await database.ExecuteAsync("""
            UPDATE topology.premises
            SET neighborhood_transformer_id = (SELECT neighborhood_transformer_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-XFMR-2')
            WHERE premise_code = 'FXD-HOME-05'
            """, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        using var then = await GetTreeAsync(client, "FXD-SUB", before);
        using var now = await GetTreeAsync(client, "FXD-SUB");

        Assert.Equal(5, PremisesUnder(then, "FXD-XFMR-1"));
        Assert.Equal(4, PremisesUnder(now, "FXD-XFMR-1"));
    }

    [Fact]
    public async Task An_unknown_substation_is_404()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/substations/NO-SUCH-SUB/tree", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.NotFound, GridTopologyErrors.SubstationNotFound);
    }

    [Fact]
    public async Task A_tree_over_the_size_limit_is_400()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        // Fixture D holds 1 + 2 + 6 + 22 + 1 nodes and 24 assets = 56.
        await using var host = new TopologyHostFactory(database.ConnectionString,
            new Dictionary<string, string?> { ["GridTopology:MaxSubstationTreeSize"] = "55" });
        using var client = host.CreateClient();

        var tooLarge = await client.GetAsync("/api/topology/substations/FXD-SUB/tree", Token);
        var small = await client.GetAsync("/api/topology/substations/FXA-SUB/tree", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(tooLarge, HttpStatusCode.BadRequest, GridTopologyErrors.TreeTooLarge);
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
    }

    [Fact]
    public async Task A_tree_exactly_at_the_size_limit_is_returned()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString,
            new Dictionary<string, string?> { ["GridTopology:MaxSubstationTreeSize"] = "56" });
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/substations/FXD-SUB/tree", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_as_of_is_400()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/substations/FXD-SUB/tree?asOf=2026-01-01T00:00:00", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.AsOfInvalid);
    }

    [Fact]
    public async Task The_database_down_during_a_read_is_a_sanitized_503_and_the_host_recovers()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var outage = new DatabaseOutage { Unreachable = true };
        await using var host = new TopologyHostFactory(database.ConnectionString, configureServices: services =>
            services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(outage)));
        using var client = host.CreateClient();

        var during = await client.GetAsync("/api/topology/substations/FXD-SUB/tree", Token);
        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(during, HttpStatusCode.ServiceUnavailable, "Rva.DatabaseUnavailable");
        ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);

        outage.Unreachable = false;
        var after = await client.GetAsync("/api/topology/substations/FXD-SUB/tree", Token);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private static async Task<JsonDocument> GetTreeAsync(HttpClient client, string code, DateTimeOffset? asOf = null)
    {
        var query = asOf is { } at ? "?asOf=" + Uri.EscapeDataString(at.ToString("O", System.Globalization.CultureInfo.InvariantCulture)) : "";
        var response = await client.GetAsync($"/api/topology/substations/{code}/tree{query}", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    private static int PremisesUnder(JsonDocument tree, string transformerCode) =>
        tree.RootElement.GetProperty("substation").GetProperty("feeders").EnumerateArray()
            .SelectMany(f => f.GetProperty("transformers").EnumerateArray())
            .Single(x => x.GetProperty("code").GetString() == transformerCode)
            .GetProperty("premises").GetArrayLength();

    private static int CountAssets(JsonElement substation) =>
        substation.GetProperty("assets").GetArrayLength()
        + substation.GetProperty("feeders").EnumerateArray().Sum(f =>
            f.GetProperty("assets").GetArrayLength()
            + f.GetProperty("premises").EnumerateArray().Sum(p => p.GetProperty("assets").GetArrayLength())
            + f.GetProperty("transformers").EnumerateArray().Sum(x =>
                x.GetProperty("assets").GetArrayLength()
                + x.GetProperty("premises").EnumerateArray().Sum(p => p.GetProperty("assets").GetArrayLength())));

    private static void AssertPoint(JsonElement geometry, double longitude, double latitude)
    {
        Assert.Equal("Point", geometry.GetProperty("type").GetString());
        var coordinates = geometry.GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble()).ToArray();
        Assert.Equal(new[] { longitude, latitude }, coordinates);
    }
}
