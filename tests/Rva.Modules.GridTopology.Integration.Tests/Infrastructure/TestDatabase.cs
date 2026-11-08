using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>One test's own database, with helpers to inspect it directly (outside the module).</summary>
public sealed class TestDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    /// <summary>The database's name, ex. for psql inside the container.</summary>
    public string Name { get; } = new NpgsqlConnectionStringBuilder(connectionString).Database
        ?? throw new ArgumentException("The connection string names no database.", nameof(connectionString));

    /// <summary>The same database, with extra connection string settings, ex. "Command Timeout=2".</summary>
    public string ConnectionStringWith(string settings) => ConnectionString + ";" + settings;

    public async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>A bare module context on this database, without the module's interceptors.</summary>
    internal GridTopologyDbContext CreateContext() =>
        new(((DbContextOptionsBuilder<GridTopologyDbContext>)GridTopologyDbContext.Configure(new DbContextOptionsBuilder<GridTopologyDbContext>(), ConnectionString)).Options);

    /// <summary>
    /// Closes the idle pooled connections to this database. Each test database has its own pool, and idle connections
    /// would otherwise stay open after the test, adding up to the server's connection limit over a run.
    /// </summary>
    public static void ReleaseConnections(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    /// <summary>The database clock (<c>clock_timestamp()</c>), which stamps every version, in UTC.</summary>
    public async Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) =>
        new(DateTime.SpecifyKind(await ScalarAsync<DateTime>("SELECT clock_timestamp()", cancellationToken), DateTimeKind.Utc));

    /// <summary>Rows in a table, ex. a _history table, for "nothing was written" checks (U1).</summary>
    public Task<long> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<long>($"SELECT count(*) FROM {table}", cancellationToken);
}
