using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Integration.Tests.Persistence;

/// <summary>
/// D-20: grid state events are append-only. FI-5, a direct context write that skips the domain, shows the database
/// rejects an update or delete (P0001 from <c>topology.reject_change</c>), that the rejection is classified unexpected,
/// not an <see cref="Error"/>, and is logged; and the module maps no route that could change or remove an event.
/// </summary>
public class GridStateEventAppendOnlyTests(PostgresContainerFixture postgres)
{
    private const string Events = "topology.grid_state_events";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task D20_updating_an_event_is_rejected_by_the_database_and_classified_unexpected()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync(GridStateEventSql.Node("feeder", "FXD-FDR", "connected_deenergized"));
        var reason = await context.Database.ScalarAsync<string>($"SELECT reason FROM {Events}", Token);

        var thrown = await RunAsync(context, "test: update grid state event", async (db, cancellationToken) =>
        {
            var gridStateEvent = await db.GridStateEvents.SingleAsync(cancellationToken);
            db.Entry(gridStateEvent).Property(e => e.Reason).CurrentValue = "tampered";
        });

        AssertRejectedAsAppendOnly(thrown);
        Assert.Equal(reason, await context.Database.ScalarAsync<string>($"SELECT reason FROM {Events}", Token));
    }

    [Fact]
    public async Task D20_deleting_an_event_is_rejected_by_the_database_and_classified_unexpected()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync(GridStateEventSql.Asset("FXA-ASSET", "disconnected"));

        var thrown = await RunAsync(context, "test: delete grid state event", async (db, cancellationToken) =>
            db.GridStateEvents.Remove(await db.GridStateEvents.SingleAsync(cancellationToken)));

        AssertRejectedAsAppendOnly(thrown);
        Assert.Equal(1L, await context.Database.CountAsync(Events, Token));
    }

    [Fact]
    public async Task D20_an_edit_reaching_the_database_through_a_request_is_a_sanitized_500_logged_once()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await database.ExecuteAsync(GridStateEventSql.Node("feeder", "FXD-FDR", "connected_deenergized"), Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.PostAsync(TestEndpoints.TamperGridStateEvent, null, Token);

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.InternalServerError, "Rva.UnexpectedError");
        var logged = ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);
        Assert.Contains(Chain(logged.Exception!), e => e is PostgresException { SqlState: PostgresErrorCodes.RaiseException });
        Assert.Equal(0L, await database.ScalarAsync<long>($"SELECT count(*) FROM {Events} WHERE reason = 'tampered'", Token));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);   // U4
    }

    [Fact]
    public async Task D20_the_module_maps_no_route_that_changes_or_removes_a_grid_state_event()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("grid-state-events", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        // POST /api/topology/grid-state-events records an event; nothing else touches the events.
        var route = Assert.Single(routes);
        Assert.Equal("/api/topology/grid-state-events", route.RoutePattern.RawText);
        Assert.Equal(new[] { "POST" }, route.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods);
    }

    /// <summary>Runs <paramref name="change"/> through the module's unit of work and returns what it threw.</summary>
    private static async Task<Exception> RunAsync(ReadContext context, string reason, Func<GridTopologyDbContext, CancellationToken, Task> change)
    {
        await using var scope = context.Module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

        // An unexpected failure is an exception, never a Result failure.
        return await Assert.ThrowsAnyAsync<Exception>(() => unitOfWork.ExecuteAsync(reason, async cancellationToken =>
        {
            await change(db, cancellationToken);
            return Result.Success();
        }, Token));
    }

    private static void AssertRejectedAsAppendOnly(Exception thrown)
    {
        Assert.Contains(Chain(thrown), e => e is PostgresException { SqlState: PostgresErrorCodes.RaiseException });
        Assert.Null(DatabaseErrorClassifier.ClassifyExpected(thrown));
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
    }
}
