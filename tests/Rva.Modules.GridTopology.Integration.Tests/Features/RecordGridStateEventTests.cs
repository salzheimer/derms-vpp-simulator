using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// D-03, D-10 to D-16, D-21: <c>POST /api/topology/grid-state-events</c> through the real host on fixtures A-D. Every
/// rejected request is problem+json with the module's code, and writes no event.
/// </summary>
public class RecordGridStateEventTests(PostgresContainerFixture postgres)
{
    private const string Route = "/api/topology/grid-state-events";
    private const string Events = "topology.grid_state_events";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Well in the past by either clock, so only the case under test can fail a request.
    private static DateTimeOffset Recently => DateTimeOffset.UtcNow.AddMinutes(-10);

    // ------------------------------------------------------------------ D-03: success

    // Node events in any state (islanded only where the node can island: GT-17), and the two asset states D10 allows.
    [Theory]
    [InlineData(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedDeenergized)]
    [InlineData(GridNodeLevels.Feeder, "FXD-FDR-2", GridStates.Disconnected)]
    [InlineData(GridNodeLevels.Substation, "FXA-SUB", GridStates.ConnectedDeenergized)]
    [InlineData(GridNodeLevels.NeighborhoodTransformer, "FXD-XFMR-1", GridStates.ConnectedEnergized)]
    [InlineData(GridNodeLevels.Premise, "FXD-SCHOOL", GridStates.Islanded)]          // a premise with can_island
    [InlineData(GridNodeLevels.Microgrid, "FXD-MG", GridStates.Islanded)]
    [InlineData("asset", "FXD-HOME-01-BESS", GridStates.Disconnected)]
    [InlineData("asset", "FXD-HOME-01-BESS", GridStates.ConnectedEnergized)]         // reconnect
    public async Task D03_an_event_is_stored_with_its_field_time_and_the_change_context(string level, string code, string state)
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();
        // A field time a few minutes back, to the microsecond (timestamptz precision).
        var occurredAt = Truncate((await database.NowAsync(Token)).AddMinutes(-3));
        var before = await database.NowAsync(Token);

        var body = level == "asset"
            ? Body(node: null, assetCode: code, state, occurredAt, "field crew report")
            : Body(node: new { level, code }, assetCode: null, state, occurredAt, "field crew report");
        var response = await client.PostAsJsonAsync(Route, body, Token);
        var after = await database.NowAsync(Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var target = level == "asset"
            ? $"asset_id = (SELECT asset_id FROM topology.assets WHERE asset_code = '{code}')"
            : $"grid_node_id = (SELECT grid_node_id FROM topology.{Table(level)} WHERE {Table(level)[..^1]}_code = '{code}')";
        Assert.Equal(1L, await database.CountAsync(Events, Token));
        Assert.Equal(1L, await database.ScalarAsync<long>($"SELECT count(*) FROM {Events} WHERE {target}", Token));
        Assert.Equal(state, await database.ScalarAsync<string>(
            $"SELECT s.internal_code FROM {Events} e JOIN topology.grid_states s USING (grid_state_id)", Token));
        Assert.Equal(occurredAt, Utc(await database.ScalarAsync<DateTime>($"SELECT occurred_at FROM {Events}", Token)));
        var recordedAt = Utc(await database.ScalarAsync<DateTime>($"SELECT recorded_at FROM {Events}", Token));
        Assert.InRange(recordedAt, before, after);
        Assert.Equal("field crew report", await database.ScalarAsync<string>($"SELECT reason FROM {Events}", Token));
        Assert.Equal(SystemActor.Id, await database.ScalarAsync<Guid>($"SELECT actor_id FROM {Events}", Token));
        Assert.Equal(SystemActor.ActorType, await database.ScalarAsync<string>($"SELECT actor_type FROM {Events}", Token));
    }

    // ------------------------------------------------------------------ D-10 to D-16: expected failures

    [Fact]
    public async Task D10_an_event_that_occurred_in_the_future_is_400_and_never_reaches_the_database()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var commands = CommandHook.Recorder();
        await using var host = HostWith(database, commands);
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, null, GridStates.ConnectedDeenergized, DateTimeOffset.UtcNow.AddDays(1), "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.OccurredInFuture);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
        // The domain check (app clock) refuses it, so no insert was attempted.
        Assert.DoesNotContain(commands.Commands, IsEventInsert);
    }

    [Theory]
    [InlineData(GridNodeLevels.Feeder, "NO-SUCH-FEEDER")]
    [InlineData(GridNodeLevels.Feeder, "FXD-XFMR-1")]          // exists, but as a transformer: codes are unique within a level only
    [InlineData(GridNodeLevels.Microgrid, "FXD-SUB")]          // exists, but as a substation
    [InlineData("asset", "NO-SUCH-ASSET")]
    public async Task D11_an_unknown_target_node_or_asset_is_422(string level, string code)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var body = level == "asset"
            ? Body(null, code, GridStates.Disconnected, Recently, "breaker trip")
            : Body(new { level, code }, null, GridStates.ConnectedDeenergized, Recently, "breaker trip");
        var response = await client.PostAsJsonAsync(Route, body, Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.UnprocessableEntity, GridTopologyErrors.GridStateTargetNotFound);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task D12_an_event_with_both_a_node_and_an_asset_target_is_400()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, "FXD-COMM-BESS", GridStates.Disconnected, Recently, "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.GridStateTargetInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task D12_an_event_with_no_target_is_400()
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(null, null, GridStates.Disconnected, Recently, "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.GridStateTargetInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Theory]
    [InlineData(GridNodeLevels.NeighborhoodTransformer, "FXD-XFMR-1")]
    [InlineData(GridNodeLevels.Feeder, "FXD-FDR")]
    [InlineData(GridNodeLevels.Premise, "FXD-HOME-01")]        // can_island = FALSE
    public async Task D13_islanded_on_a_node_that_cannot_island_is_422(string level, string code)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level, code }, null, GridStates.Islanded, Recently, "storm"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.UnprocessableEntity, GridTopologyErrors.NodeCannotIsland);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Theory]
    [InlineData(GridStates.Islanded)]
    [InlineData(GridStates.ConnectedDeenergized)]
    public async Task D14_an_asset_targeted_state_other_than_disconnected_or_reconnected_is_422(string state)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(null, "FXD-HOME-01-BESS", state, Recently, "inverter report"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.UnprocessableEntity, GridTopologyErrors.AssetGridStateNotAllowed);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Theory]
    [InlineData("on_fire")]
    [InlineData("energized")]
    public async Task D15_an_unknown_grid_state_code_is_400(string state)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, null, state, Recently, "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.GridStateUnknown);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task D16_an_event_without_a_reason_is_400(string? reason)
    {
        var (database, host) = await StartAsync();
        await using var _ = host;
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, null, GridStates.ConnectedDeenergized, Recently, reason), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.ChangeReasonInvalid);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
    }

    // ------------------------------------------------------------------ D-21: clock skew (Q2)

    [Fact]
    public async Task D21_with_the_app_clock_ahead_the_database_not_future_check_fires_and_is_the_same_400()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var commands = CommandHook.Recorder();
        // FI-8: the app clock an hour ahead of the database clock.
        await using var host = HostWith(database, commands, services =>
            services.AddSingleton<TimeProvider>(new ShiftedTime(TimeSpan.FromHours(1))));
        using var client = host.CreateClient();
        // Not in the future by the app clock, but in the future by the database clock that stamps recorded_at.
        var occurredAt = (await database.NowAsync(Token)).AddMinutes(5);

        var response = await client.PostAsJsonAsync(Route,
            Body(new { level = GridNodeLevels.Feeder, code = "FXD-FDR" }, null, GridStates.ConnectedDeenergized, occurredAt, "breaker trip"), Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.BadRequest, GridTopologyErrors.OccurredInFuture);
        Assert.Equal(0L, await database.CountAsync(Events, Token));
        // The fault was hit: the insert reached the database, whose ck_grid_state_events_not_future rejected it; it is
        // an expected failure, so nothing is logged at Error.
        Assert.Contains(commands.Commands, IsEventInsert);
        Assert.Empty(host.Logs.Errors);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The request body in its JSON shape (camelCase); <paramref name="node"/> is <c>{ level, code }</c>.</summary>
    private static object Body(object? node, string? assetCode, string? state, DateTimeOffset occurredAt, string? reason) => new
    {
        node,
        assetCode,
        state,
        occurredAt = occurredAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        reason,
    };

    private async Task<(TestDatabase Database, TopologyHostFactory Host)> StartAsync()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        return (database, new TopologyHostFactory(database.ConnectionString));
    }

    private static TopologyHostFactory HostWith(TestDatabase database, CommandHook commands, Action<IServiceCollection>? configure = null) =>
        new(database.ConnectionString, configureServices: services =>
        {
            services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(commands));
            configure?.Invoke(services);
        });

    private static bool IsEventInsert(string sql) =>
        sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) && sql.Contains("grid_state_events", StringComparison.Ordinal);

    private static DateTimeOffset Truncate(DateTimeOffset at) => new(at.UtcTicks - at.UtcTicks % 10, TimeSpan.Zero);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Table(string level) => level switch
    {
        GridNodeLevels.Substation => "substations",
        GridNodeLevels.Feeder => "feeders",
        GridNodeLevels.NeighborhoodTransformer => "neighborhood_transformers",
        GridNodeLevels.Premise => "premises",
        GridNodeLevels.Microgrid => "microgrids",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private sealed class ShiftedTime(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + shift;
    }
}
