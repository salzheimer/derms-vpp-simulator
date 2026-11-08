using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

namespace Rva.Modules.GridTopology.Integration.Tests.Parity;

/// <summary>
/// The pgTAP reference queries in infrastructure/postgres/tests/fixtures/_helpers.sql, run against a test database so
/// the C# results can be compared with the executable spec. The helpers are <c>pg_temp</c> functions and set
/// transaction-local settings, so they are created on one dedicated, unpooled connection inside a transaction that
/// is rolled back.
/// </summary>
internal static class ReferenceQueries
{
    /// <summary>One row of <c>pg_temp.asset_paths(at)</c>.</summary>
    public sealed record AssetPath(
        string AssetCode, long AssetId, string Lifecycle, string Participation,
        double RatedKva, double RatedPMaxKw, double? AssetExportLimitKw, string Interconnection,
        string? PremiseCode, double? PremiseExportLimitKw,
        string? TransformerCode, double? TransformerKva,
        string? FeederCode, double? FeederKva,
        string? SubstationCode, double? SubstationKva,
        string? MicrogridCode, double? MicrogridExportLimitKw,
        long? SubstationNode, long? FeederNode, long? MicrogridNode, long? TransformerNode, long? PremiseNode);

    public static async Task<IReadOnlyList<AssetPath>> AssetPathsAsync(TestDatabase database, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var helpers = await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.Root, "infrastructure", "postgres", "tests", "fixtures", "_helpers.sql"), cancellationToken);

        await using var connection = new NpgsqlConnection(database.ConnectionString + ";Pooling=false");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var setup = new NpgsqlCommand(helpers, connection, transaction))
            await setup.ExecuteNonQueryAsync(cancellationToken);

        await using var query = new NpgsqlCommand("""
            SELECT asset_code, asset_id, lifecycle, participation, rated_kva, rated_p_max_kw, asset_export_limit_kw, interconnection,
                   premise_code, premise_export_limit_kw, transformer_code, transformer_kva, feeder_code, feeder_kva,
                   substation_code, substation_kva, microgrid_code, microgrid_export_limit_kw,
                   substation_node, feeder_node, microgrid_node, transformer_node, premise_node
            FROM pg_temp.asset_paths($1)
            ORDER BY asset_code
            """, connection, transaction) { Parameters = { new() { Value = at.ToUniversalTime() } } };

        var rows = new List<AssetPath>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                T? Nullable<T>(int i) where T : struct => reader.IsDBNull(i) ? null : reader.GetFieldValue<T>(i);
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                rows.Add(new AssetPath(
                    reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                    reader.GetDouble(4), reader.GetDouble(5), Nullable<double>(6), reader.GetString(7),
                    Text(8), Nullable<double>(9), Text(10), Nullable<double>(11), Text(12), Nullable<double>(13),
                    Text(14), Nullable<double>(15), Text(16), Nullable<double>(17),
                    Nullable<long>(18), Nullable<long>(19), Nullable<long>(20), Nullable<long>(21), Nullable<long>(22)));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return rows;
    }
}
