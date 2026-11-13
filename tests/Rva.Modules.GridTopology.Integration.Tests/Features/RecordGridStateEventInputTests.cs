using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// <c>POST /api/topology/grid-state-events</c> inputs the reviewed tests (<c>RecordGridStateEventTests</c>) leave open,
/// added with the implementation of <c>p1-s01d</c>: a missing or malformed <c>occurredAt</c>, a malformed node target,
/// islanding a substation or a microgrid that cannot island, a non-UTC offset, and the reason length.
/// </summary>
public class RecordGridStateEventInputTests(PostgresContainerFixture postgres)
{
    private const string Route = "/api/topology/grid-state-events";
    private const string Events = "topology.grid_state_events";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Recently =>
        DateTimeOffset.UtcNow.AddMinutes(-10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-07T08:00:00")]       // no UTC offset
    [InlineData("2026-11-07")]
    [InlineData("yesterday")]
    public async Task A_missing_or_malformed_occurred_at_is_400(string? occurredAt)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new
        {
            node = new { level = GridNodeLevels.Feeder, code = "FXD-FDR" },
            assetCode = (string?)null,
            state = GridStates.ConnectedDeenergized,
            occurredAt,
            reason = "breaker trip",
        }, Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.OccurredAtInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task An_occurred_at_with_a_non_utc_offset_is_stored_as_the_same_instant()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();
        var instant = new DateTimeOffset(2026, 11, 1, 16, 30, 0, TimeSpan.Zero);

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, null, GridStates.ConnectedDeenergized, "2026-11-01T08:30:00-08:00", "breaker trip"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(instant, new DateTimeOffset(DateTime.SpecifyKind(
            await database.ScalarAsync<DateTime>($"SELECT occurred_at FROM {Events}", Token), DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("transformer", "FXD-XFMR-1")]   // not a level code
    [InlineData(null, "FXD-FDR")]
    [InlineData(GridNodeLevels.Feeder, null)]
    [InlineData(GridNodeLevels.Feeder, "  ")]
    public async Task A_node_target_without_a_known_level_and_a_code_is_400(string? level, string? code)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level, code }, null, GridStates.ConnectedDeenergized, Recently, "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.GridStateTargetInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task A_blank_asset_code_is_400()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route, Body(null, " ", GridStates.Disconnected, Recently, "inverter report"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.GridStateTargetInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task Islanded_on_a_substation_is_422()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Substation, code = "FXD-SUB" }, null, GridStates.Islanded, Recently, "storm"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.UnprocessableEntity, GridTopologyErrors.NodeCannotIsland);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task Islanded_on_a_microgrid_that_cannot_island_is_422()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        await database.ExecuteAsync("UPDATE topology.microgrids SET can_island = FALSE WHERE microgrid_code = 'FXD-MG'", Token);
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Microgrid, code = "FXD-MG" }, null, GridStates.Islanded, Recently, "storm"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.UnprocessableEntity, GridTopologyErrors.NodeCannotIsland);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task A_reason_of_up_to_255_characters_is_stored_and_a_longer_one_is_400()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();
        var node = new { level = GridNodeLevels.Feeder, code = "FXD-FDR" };

        var tooLong = await client.PostAsJsonAsync(Route, Body(node, null, GridStates.ConnectedDeenergized, Recently, new string('r', 256)), Token);
        await ProblemAssertions.AssertSanitizedProblemAsync(tooLong, HttpStatusCode.BadRequest, GridTopologyErrors.ChangeReasonInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));

        var longest = await client.PostAsJsonAsync(Route, Body(node, null, GridStates.ConnectedDeenergized, Recently, new string('r', 255)), Token);
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
        Assert.Equal(255, await database.ScalarAsync<int>($"SELECT length(reason) FROM {Events}", Token));
    }

    private static object Body(object? node, string? assetCode, string? state, string? occurredAt, string? reason) =>
        new { node, assetCode, state, occurredAt, reason };

    private async Task<(TestDatabase Database, TopologyHostFactory Host)> StartAsync()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        return (database, new TopologyHostFactory(database.ConnectionString));
    }
}
