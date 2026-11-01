using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Hosting;

/// <summary>
/// Unexpected failures through the real host in Production (plan p1-s01 section 4): each response is sanitized
/// problem+json (U2), logged once at Error (U3), leaves nothing written (U1), and the host keeps serving (U4).
/// </summary>
public class UnexpectedFailureTests(PostgresContainerFixture postgres)
{
    private const string FixtureAssetKva = $"SELECT rated_kva FROM topology.assets WHERE asset_code = '{FixtureCodes.FixtureAAsset}'";
    private const string FixtureAssetPMax = $"SELECT rated_p_max_kw FROM topology.assets WHERE asset_code = '{FixtureCodes.FixtureAAsset}'";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Database_unreachable_during_a_request_is_a_sanitized_503()
    {
        await using var host = new TopologyHostFactory(HostWiringTests.Unreachable);
        using var client = host.CreateClient();

        var response = await client.GetAsync(TestEndpoints.Read, Token);

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Rva.DatabaseUnavailable",
            "127.0.0.1", "unreachable-secret");
        ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);
    }

    [Fact]
    public async Task Requests_succeed_again_once_the_database_is_back()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var outage = new DatabaseOutage { Unreachable = true };
        await using var host = new TopologyHostFactory(database.ConnectionString, configureServices: services =>
            services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(outage)));
        using var client = host.CreateClient();

        var during = await client.GetAsync(TestEndpoints.Read, Token);
        outage.Unreachable = false;
        var after = await client.GetAsync(TestEndpoints.Read, Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(await database.ScalarAsync<long>("SELECT count(*) FROM topology.substations", Token),
            long.Parse(await after.Content.ReadAsStringAsync(Token), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Database_rejecting_a_change_the_domain_allowed_is_a_sanitized_500()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var historyBefore = await database.CountAsync("topology.assets_history", Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var response = await client.PostAsync(TestEndpoints.BreakAssetRating, null, Token);

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.InternalServerError, "Rva.UnexpectedError",
            "ck_assets_power_within_kva");
        var logged = ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);
        Assert.Contains("ck_assets_power_within_kva", logged.Exception!.ToString(), StringComparison.Ordinal);
        Assert.Equal(7.6, await database.ScalarAsync<double>(FixtureAssetKva, Token));
        Assert.Equal(historyBefore, await database.CountAsync("topology.assets_history", Token));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);
    }

    [Fact]
    public async Task Unhandled_exception_in_an_endpoint_is_a_sanitized_500()
    {
        await using var host = new TopologyHostFactory(HostWiringTests.Unreachable);
        using var client = host.CreateClient();

        var response = await client.GetAsync(TestEndpoints.Throw, Token);

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.InternalServerError, "Rva.UnexpectedError",
            TestEndpoints.SecretDetail);
        var logged = ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);
        Assert.Contains(TestEndpoints.SecretDetail, logged.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_timeout_on_a_locked_row_is_a_transient_503_and_writes_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var historyBefore = await database.CountAsync("topology.assets_history", Token);
        await using var host = new TopologyHostFactory(database.ConnectionStringWith("Command Timeout=2"));
        using var client = host.CreateClient();

        HttpResponseMessage blocked;
        await using (var locker = await database.OpenAsync(Token))
        await using (var transaction = await locker.BeginTransactionAsync(Token))
        {
            await using var lockRow = new NpgsqlCommand(
                $"SELECT 1 FROM topology.assets WHERE asset_code = '{FixtureCodes.FixtureAAsset}' FOR UPDATE", locker, transaction);
            await lockRow.ExecuteNonQueryAsync(Token);

            blocked = await client.PostAsync(TestEndpoints.UpdateAsset, null, Token);
            await transaction.RollbackAsync(Token);
        }

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(blocked, HttpStatusCode.ServiceUnavailable, "Rva.DatabaseUnavailable");
        ProblemAssertions.AssertOneErrorLogged(host.Logs, traceId);
        Assert.Equal(7.6, await database.ScalarAsync<double>(FixtureAssetPMax, Token));
        Assert.Equal(historyBefore, await database.CountAsync("topology.assets_history", Token));

        // U4: with the lock gone, the same change goes through.
        var retried = await client.PostAsync(TestEndpoints.UpdateAsset, null, Token);
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
        Assert.Equal(7.0, await database.ScalarAsync<double>(FixtureAssetPMax, Token));
    }

    [Fact]
    public async Task Missing_lookup_row_fails_readiness_and_the_command_that_needs_it()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, Token);
        await database.ExecuteAsync("DELETE FROM topology.technology_types WHERE internal_code = 'wind'", Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var ready = await client.GetAsync("/health/ready", Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("topology.technology_types.wind", await ready.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        var logsBefore = host.Logs.Errors.Count;   // the failed readiness check is itself logged
        var command = await client.GetAsync(TestEndpoints.Lookup, Token);
        await ProblemAssertions.AssertSanitizedProblemAsync(command, HttpStatusCode.InternalServerError, "Rva.UnexpectedError", "wind");
        Assert.Equal(logsBefore + 1, host.Logs.Errors.Count);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);
    }
}
