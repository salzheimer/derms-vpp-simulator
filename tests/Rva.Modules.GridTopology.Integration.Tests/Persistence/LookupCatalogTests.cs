using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Persistence;

/// <summary>A-04: the catalog resolves every seeded code in the seven lookup tables to its row's id.</summary>
public class LookupCatalogTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task Every_seeded_lookup_code_resolves_to_its_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        await using var module = ModuleServices.Create(database.ConnectionString);
        var catalog = module.Provider.GetRequiredService<LookupCatalog>();
        var checkedRows = 0;

        foreach (var table in Enum.GetValues<LookupTable>())
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                $"SELECT internal_code, {IdColumn(table)} FROM topology.{LookupCatalog.TableName(table)}", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Assert.Equal(reader.GetInt32(1), await catalog.IdOfAsync(table, reader.GetString(0), cancellationToken));
                checkedRows++;
            }
        }

        Assert.Equal(LookupCatalog.KnownCodes.Values.Sum(codes => codes.Length), checkedRows);
        Assert.Empty(LookupCatalog.MissingKnownCodes(await catalog.LoadAsync(cancellationToken)));
    }

    [Fact]
    public async Task Unknown_code_throws_naming_the_table()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        await using var module = ModuleServices.Create(database.ConnectionString);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            module.Provider.GetRequiredService<LookupCatalog>().IdOfAsync(LookupTable.GridStates, "flooded", cancellationToken));

        Assert.Contains("topology.grid_states", error.Message, StringComparison.Ordinal);
    }

    private static string IdColumn(LookupTable table) => table switch
    {
        LookupTable.GridStates => "grid_state_id",
        _ => LookupCatalog.TableName(table).TrimEnd('s') + "_id",
    };
}
