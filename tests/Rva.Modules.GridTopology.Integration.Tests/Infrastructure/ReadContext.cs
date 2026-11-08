using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// A test's own database (fixtures A-D by default) with the module's services on it, for contract-level read tests,
/// and helpers to look up ids and the database clock directly.
/// </summary>
internal sealed class ReadContext : IAsyncDisposable
{
    private ReadContext(TestDatabase database, ModuleServices module)
    {
        Database = database;
        Module = module;
    }

    public TestDatabase Database { get; }
    public ModuleServices Module { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<ReadContext> CreateAsync(
        PostgresContainerFixture postgres, TestDatabaseTemplate template = TestDatabaseTemplate.Fixtures, Action<IServiceCollection>? configure = null)
    {
        var database = await postgres.CreateDatabaseAsync(template, Token);
        return new ReadContext(database, ModuleServices.Create(database.ConnectionString, configure));
    }

    /// <summary>Runs <paramref name="call"/> against the contract in a new scope, as another module would.</summary>
    public async Task<T> QueryAsync<T>(Func<IGridTopologyQueries, Task<T>> call)
    {
        await using var scope = Module.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<IGridTopologyQueries>());
    }

    public async Task<AssetId> AssetIdAsync(string code) =>
        new(await Database.ScalarAsync<long>($"SELECT asset_id FROM topology.assets WHERE asset_code = '{code}'", Token));

    /// <summary>The grid node id of a node by table (ex. <c>premises</c>) and natural code.</summary>
    public async Task<GridNodeId> GridNodeIdAsync(string table, string code) =>
        new(await Database.ScalarAsync<long>(
            $"SELECT grid_node_id FROM topology.{table} WHERE {CodeColumn(table)} = '{code}'", Token));

    /// <summary>The database clock: the time a version written after this call is newer than.</summary>
    public Task<DateTimeOffset> NowAsync() => Database.NowAsync(Token);

    public Task ExecuteAsync(string sql) => Database.ExecuteAsync(sql, Token);

    public async ValueTask DisposeAsync()
    {
        await Module.DisposeAsync();
        TestDatabase.ReleaseConnections(Database.ConnectionString);
    }

    private static string CodeColumn(string table) => table switch
    {
        "substations" => "substation_code",
        "feeders" => "feeder_code",
        "neighborhood_transformers" => "neighborhood_transformer_code",
        "premises" => "premise_code",
        "microgrids" => "microgrid_code",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };
}
