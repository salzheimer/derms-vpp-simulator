using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Hosting;

/// <summary>
/// The module in the real host: wiring (A-07), health with and without a database (A-08, A-28), the time zone setting
/// (A-29), and problem+json for unknown routes.
/// </summary>
public class HostWiringTests(PostgresContainerFixture postgres)
{
    // Nothing listens on port 1; the password must never appear in a response.
    internal const string Unreachable = "Host=127.0.0.1;Port=1;Database=rva_core;Username=postgres;Password=unreachable-secret;Timeout=2";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Host_starts_with_the_module_and_resolves_its_services()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        await using var scope = host.Services.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGridTopologyQueries>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UnitOfWork>());
    }

    [Fact]
    public async Task Liveness_and_readiness_are_healthy_with_a_database()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var live = await client.GetAsync("/health", Token);
        var ready = await client.GetAsync("/health/ready", Token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var report = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Token));
        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());
        Assert.Contains(report.RootElement.GetProperty("checks").EnumerateArray(), c => c.GetProperty("name").GetString() == ReadinessCheck.Name);
    }

    [Fact]
    public async Task Host_starts_without_a_database_and_reports_not_ready()
    {
        await using var host = new TopologyHostFactory(Unreachable);
        using var client = host.CreateClient();

        var live = await client.GetAsync("/health", Token);
        var ready = await client.GetAsync("/health/ready", Token);
        var readyBody = await ready.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("rva_core is unreachable", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable-secret", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", readyBody, StringComparison.Ordinal);
        // The host keeps serving.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);
    }

    [Theory]
    [InlineData("", "GridTopology:UtilityTimeZone is required.")]
    [InlineData("Mars/Olympus_Mons", "GridTopology:UtilityTimeZone must be an IANA time zone id")]
    public async Task Missing_or_invalid_utility_time_zone_fails_startup_naming_the_key(string value, string expected)
    {
        await using var host = new TopologyHostFactory(Unreachable, new Dictionary<string, string?> { ["GridTopology:UtilityTimeZone"] = value });

        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains(error.Failures, f => f.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_route_is_problem_json_with_a_code_and_trace_id()
    {
        await using var host = new TopologyHostFactory(Unreachable);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/api/topology/no-such-route", Token);

        await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.NotFound, "Rva.NotFound");
    }
}
