using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

namespace Rva.Modules.GridTopology.Integration.Tests.Persistence;

/// <summary>
/// A-03 and A-03b: the EF model matches rva_core_init.sql, the schema's source of truth (D6). Catches drift in either
/// direction: a mapped column the schema lacks, a type or nullability mismatch, or a table or column added to the
/// schema but not mapped.
/// </summary>
public class SchemaMappingTests(PostgresContainerFixture postgres)
{
    private sealed record Column(string Type, bool NotNull);

    private static string Normalize(string type) => type.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private async Task<(TestDatabase Database, Dictionary<(string Table, string Column), Column> Columns)> LoadSchemaAsync(CancellationToken cancellationToken)
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        var columns = new Dictionary<(string, string), Column>();

        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'topology' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns[(reader.GetString(0), reader.GetString(1))] = new Column(reader.GetString(2), reader.GetBoolean(3));

        return (database, columns);
    }

    private static IEnumerable<(IEntityType Entity, StoreObjectIdentifier Table)> MappedTables(DbContext context) =>
        context.Model.GetEntityTypes().Select(e => (e, StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema())));

    [Fact]
    public async Task Every_mapped_column_exists_with_a_compatible_type_and_nullability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (database, columns) = await LoadSchemaAsync(cancellationToken);
        await using var context = database.CreateContext();
        var problems = new List<string>();

        foreach (var (entity, table) in MappedTables(context))
        {
            Assert.Equal("topology", table.Schema);
            foreach (var property in entity.GetProperties())
            {
                var name = property.GetColumnName(table)!;
                if (name == "xmin")
                    continue;   // system column (row version), not in pg_attribute's user columns

                var where = $"{table.Name}.{name} ({entity.ClrType.Name}.{property.Name})";
                if (!columns.TryGetValue((table.Name, name), out var column))
                {
                    problems.Add($"{where}: no such column");
                    continue;
                }
                if (Normalize(property.GetColumnType(table)) != Normalize(column.Type))
                    problems.Add($"{where}: mapped as {property.GetColumnType(table)}, column is {column.Type}");
                if (property.IsColumnNullable(table) == column.NotNull)
                    problems.Add($"{where}: mapped {(property.IsColumnNullable(table) ? "nullable" : "required")}, column is {(column.NotNull ? "NOT NULL" : "NULL")}");
            }
        }

        Assert.True(problems.Count == 0, "EF mapping does not match rva_core:\n" + string.Join("\n", problems));
    }

    [Fact]
    public async Task Every_topology_table_and_column_is_mapped_except_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (database, columns) = await LoadSchemaAsync(cancellationToken);
        await using var context = database.CreateContext();

        var mapped = MappedTables(context)
            .SelectMany(m => m.Entity.GetProperties().Select(p => (m.Table.Name, p.GetColumnName(m.Table)!)))
            .ToHashSet();
        var unmapped = columns.Keys
            .Where(k => !k.Table.EndsWith("_history", StringComparison.Ordinal) && !mapped.Contains(k))
            .Select(k => $"{k.Table}.{k.Column}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unmapped.Count == 0, "Columns in rva_core's topology schema with no EF mapping:\n" + string.Join("\n", unmapped));
    }

    [Fact]
    public async Task History_tables_are_not_mapped()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();

        Assert.DoesNotContain(MappedTables(context), m => m.Table.Name.EndsWith("_history", StringComparison.Ordinal));
    }
}
