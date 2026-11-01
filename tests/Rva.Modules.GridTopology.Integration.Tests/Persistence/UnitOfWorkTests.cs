using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;
using Rva.Modules.GridTopology.Persistence;
using Rva.Modules.GridTopology.Persistence.Configurations;
using Rva.SharedKernel.Results;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Integration.Tests.Persistence;

/// <summary>
/// Writes through <see cref="UnitOfWork"/> against real PostgreSQL: change context (A-05, A-22), database-generated
/// columns (A-06), concurrency (A-11), error classification (A-12), the save guard (A-21), and cancellation (A-25).
/// </summary>
public class UnitOfWorkTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string AssetId = $"(SELECT asset_id FROM topology.assets WHERE asset_code = '{FixtureCodes.FixtureAAsset}')";

    private static Task<Result> ChangeFixtureAssetAsync(AsyncServiceScope scope, string reason, Action<GridTopologyDbContext, Asset> change, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        return scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteAsync(reason, async ct =>
        {
            var asset = await db.Assets.SingleAsync(a => a.Code == FixtureCodes.FixtureAAsset, ct);
            change(db, asset);
            return Result.Success();
        }, cancellationToken);
    }

    [Fact]
    public async Task Write_records_the_system_actor_and_reason_in_history()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var module = ModuleServices.Create(database.ConnectionString);
        await using var scope = module.CreateScope();

        var result = await ChangeFixtureAssetAsync(scope, "A-05 rating correction",
            (db, asset) => db.Entry(asset).Property(a => a.RatedPMax).CurrentValue = new Kilowatts(7.0), Token);

        Assert.True(result.IsSuccess);
        await using var connection = await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand($"""
            SELECT operation, changed_by, actor_type, change_reason, rated_p_max_kw
            FROM topology.assets_history WHERE asset_id = {AssetId}
            ORDER BY upper(sys_period) DESC LIMIT 1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(("UPDATE", SystemActor.Id, "system", "A-05 rating correction", 7.6),
            (reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
        Assert.Equal(7.0, await database.ScalarAsync<double>($"SELECT rated_p_max_kw FROM topology.assets WHERE asset_id = {AssetId}", Token));
    }

    [Fact]
    public async Task Database_generated_columns_are_never_written_and_are_read_back()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var recorder = CommandHook.Recorder();
        await using var module = ModuleServices.Create(database.ConnectionString,
            services => services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(recorder)));
        await using var scope = module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();

        var asset = await TestAssets.PlannedBatteryAtFixtureAAsync(scope.ServiceProvider, "A06-ASSET", Token);
        var inserted = await scope.ServiceProvider.GetRequiredService<UnitOfWork>()
            .ExecuteAsync("A-06 insert", _ => { db.Assets.Add(asset); return Task.FromResult(Result.Success()); }, Token);
        var updated = await scope.ServiceProvider.GetRequiredService<UnitOfWork>()
            .ExecuteAsync("A-06 update", _ => { db.Entry(asset).Property(a => a.RatedPMax).CurrentValue = new Kilowatts(4); return Task.FromResult(Result.Success()); }, Token);

        Assert.True(inserted.IsSuccess && updated.IsSuccess);
        var createdAt = db.Entry(asset).Property<DateTimeOffset>(MappingExtensions.CreatedAt).CurrentValue;
        var sysPeriod = db.Entry(asset).Property<NpgsqlRange<DateTime>>(MappingExtensions.SysPeriod).CurrentValue;
        Assert.NotEqual(default, createdAt);
        Assert.True(sysPeriod.LowerBound > createdAt.UtcDateTime, "the update started a new version after the insert");

        // Only the column lists the statements set, not their RETURNING clauses.
        var written = recorder.Commands
            .Where(c => c.Contains("INSERT INTO topology.", StringComparison.Ordinal) || c.Contains("UPDATE topology.", StringComparison.Ordinal))
            .Select(c => c.Contains("RETURNING", StringComparison.Ordinal) ? c[..c.IndexOf("RETURNING", StringComparison.Ordinal)] : c)
            .ToList();
        Assert.NotEmpty(written);
        Assert.All(written, sql =>
        {
            Assert.DoesNotContain("created_at", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("sys_period", sql, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Grid_node_id_is_assigned_by_the_database()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, Token);
        var recorder = CommandHook.Recorder();
        await using var module = ModuleServices.Create(database.ConnectionString,
            services => services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(recorder)));
        await using var scope = module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var substation = new Domain.Hierarchy.Substation("A06-SUB", 20000);

        var result = await scope.ServiceProvider.GetRequiredService<UnitOfWork>()
            .ExecuteAsync("A-06 node", _ => { db.Substations.Add(substation); return Task.FromResult(Result.Success()); }, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(await database.ScalarAsync<long>("SELECT grid_node_id FROM topology.substations WHERE substation_code = 'A06-SUB'", Token),
            substation.GridNodeId.Value);
        var insert = Assert.Single(recorder.Commands, c => c.Contains("INSERT INTO topology.substations", StringComparison.Ordinal));
        Assert.DoesNotContain("grid_node_id", insert[..insert.IndexOf("RETURNING", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_of_two_concurrent_updates_is_a_conflict()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var module = ModuleServices.Create(database.ConnectionString);
        await using var first = module.CreateScope();
        await using var second = module.CreateScope();
        var historyBefore = await database.CountAsync("topology.assets_history", Token);

        // Both read the same version before either writes.
        var firstDb = first.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        await firstDb.Assets.SingleAsync(a => a.Code == FixtureCodes.FixtureAAsset, Token);
        await secondDb.Assets.SingleAsync(a => a.Code == FixtureCodes.FixtureAAsset, Token);

        var won = await ChangeFixtureAssetAsync(first, "A-11 first", (db, a) => db.Entry(a).Property(x => x.RatedPMax).CurrentValue = new Kilowatts(7.0), Token);
        var lost = await ChangeFixtureAssetAsync(second, "A-11 second", (db, a) => db.Entry(a).Property(x => x.RatedPMax).CurrentValue = new Kilowatts(6.0), Token);

        Assert.True(won.IsSuccess);
        Assert.Equal((ErrorKind.Conflict, GridTopologyErrors.ConcurrentChange), (lost.Error.Kind, lost.Error.Code));
        Assert.Equal(historyBefore + 1, await database.CountAsync("topology.assets_history", Token));
        Assert.Equal(7.0, await database.ScalarAsync<double>($"SELECT rated_p_max_kw FROM topology.assets WHERE asset_id = {AssetId}", Token));
    }

    [Fact]
    public async Task Duplicate_asset_code_is_classified_as_a_conflict()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var module = ModuleServices.Create(database.ConnectionString);
        await using var scope = module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var duplicate = await TestAssets.PlannedBatteryAtFixtureAAsync(scope.ServiceProvider, FixtureCodes.FixtureAAsset, Token);
        var assetsBefore = await database.CountAsync("topology.assets", Token);
        var componentsBefore = await database.CountAsync("topology.asset_components", Token);

        var result = await scope.ServiceProvider.GetRequiredService<UnitOfWork>()
            .ExecuteAsync("A-12 duplicate", _ => { db.Assets.Add(duplicate); return Task.FromResult(Result.Success()); }, Token);

        Assert.Equal((ErrorKind.Conflict, GridTopologyErrors.DuplicateCode), (result.Error.Kind, result.Error.Code));
        Assert.Equal(assetsBefore, await database.CountAsync("topology.assets", Token));
        Assert.Equal(componentsBefore, await database.CountAsync("topology.asset_components", Token));
        Assert.Contains(module.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(GridTopologyErrors.DuplicateCode, StringComparison.Ordinal));
        Assert.DoesNotContain(module.Logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Classifier_names_the_constraints_the_schema_actually_has()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, Token);

        foreach (var name in new[] { DatabaseErrorClassifier.AssetCodeUnique, DatabaseErrorClassifier.GridStateEventNotFuture })
            Assert.Equal(1L, await database.ScalarAsync<long>(
                $"SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'topology' AND c.conname = '{name}'", Token));
    }

    [Fact]
    public async Task SaveChanges_outside_a_unit_of_work_throws_and_writes_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var module = ModuleServices.Create(database.ConnectionString);
        await using var scope = module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var historyBefore = await database.CountAsync("topology.assets_history", Token);

        var asset = await db.Assets.SingleAsync(a => a.Code == FixtureCodes.FixtureAAsset, Token);
        db.Entry(asset).Property(a => a.RatedPMax).CurrentValue = new Kilowatts(7.0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        Assert.Equal(7.6, await database.ScalarAsync<double>($"SELECT rated_p_max_kw FROM topology.assets WHERE asset_id = {AssetId}", Token));
        Assert.Equal(historyBefore, await database.CountAsync("topology.assets_history", Token));
    }

    [Fact]
    public async Task Malformed_actor_fails_the_transaction_before_anything_is_written()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        await using var module = ModuleServices.Create(database.ConnectionString);
        await using var scope = module.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>();
        var historyBefore = await database.CountAsync("topology.assets_history", Token);
        scope.ServiceProvider.GetRequiredService<ChangeContextAccessor>().Current = new ChangeContext("not-a-uuid", "system", "A-22");

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.BeginTransactionAsync(Token));

        Assert.Equal(PostgresErrorCodes.InvalidTextRepresentation, error.SqlState);
        Assert.Equal(historyBefore, await database.CountAsync("topology.assets_history", Token));
        Assert.Equal(7.6, await database.ScalarAsync<double>($"SELECT rated_p_max_kw FROM topology.assets WHERE asset_id = {AssetId}", Token));
    }

    [Fact]
    public async Task Cancellation_during_save_commits_nothing_and_logs_at_information()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new CommandHook(sql => sql.Contains("UPDATE topology.assets", StringComparison.Ordinal), caller.Cancel);
        await using var module = ModuleServices.Create(database.ConnectionString,
            services => services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(hook)));
        await using var scope = module.CreateScope();
        var historyBefore = await database.CountAsync("topology.assets_history", Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChangeFixtureAssetAsync(scope, "A-25 cancelled",
            (db, a) => db.Entry(a).Property(x => x.RatedPMax).CurrentValue = new Kilowatts(7.0), caller.Token));

        Assert.Equal(7.6, await database.ScalarAsync<double>($"SELECT rated_p_max_kw FROM topology.assets WHERE asset_id = {AssetId}", Token));
        Assert.Equal(historyBefore, await database.CountAsync("topology.assets_history", Token));
        Assert.Contains(module.Logs.Entries, e => e.Level == LogLevel.Information && e.Category.EndsWith(nameof(UnitOfWork), StringComparison.Ordinal));
        Assert.DoesNotContain(module.Logs.Entries, e => e.Level >= LogLevel.Error);
    }
}
