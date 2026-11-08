using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Domain.Catalog;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>The topology lookup tables the module reads ids from by code.</summary>
internal enum LookupTable
{
    TechnologyTypes,
    InterconnectionTypes,
    CustomerSegments,
    GridNodeTypes,
    AssetLifecycleStates,
    ParticipationStates,
    GridStates,
}

/// <summary>
/// Lookup ids by code, so module code never relies on identity values. Loaded on first use, not at startup, so the
/// host starts without a database (D16), and then kept for the life of the process (D22): adding a lookup row needs a
/// restart. A failed load is not cached; the next call tries again.
/// </summary>
internal sealed class LookupCatalog(IServiceScopeFactory scopeFactory)
{
    /// <summary>The codes module code relies on: the rows rva_core_init.sql seeds. Readiness fails if any is missing.</summary>
    public static readonly IReadOnlyDictionary<LookupTable, string[]> KnownCodes = new Dictionary<LookupTable, string[]>
    {
        [LookupTable.TechnologyTypes] = ["bess", "solar_pv", "wind", "evse"],
        [LookupTable.InterconnectionTypes] = ["btm", "ftm", "atm"],
        [LookupTable.CustomerSegments] = ["residential", "commercial_industrial"],
        [LookupTable.GridNodeTypes] = ["substation", "feeder", "neighborhood_transformer", "premise", "microgrid"],
        [LookupTable.AssetLifecycleStates] = ["planned", "installed", "commissioned", "decommissioned"],
        [LookupTable.ParticipationStates] = ["not_enrolled", "active", "suspended", "opted_out"],
        [LookupTable.GridStates] = ["connected_energized", "connected_deenergized", "islanded", "disconnected"],
    };

    private readonly SemaphoreSlim loadGate = new(1, 1);
    private IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<string, int>>? loaded;
    private LookupCodes? codes;

    public static string TableName(LookupTable table) => table switch
    {
        LookupTable.TechnologyTypes => "technology_types",
        LookupTable.InterconnectionTypes => "interconnection_types",
        LookupTable.CustomerSegments => "customer_segments",
        LookupTable.GridNodeTypes => "grid_node_types",
        LookupTable.AssetLifecycleStates => "asset_lifecycle_states",
        LookupTable.ParticipationStates => "participation_states",
        LookupTable.GridStates => "grid_states",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    /// <summary>
    /// The id of <paramref name="code"/> in <paramref name="table"/>. A missing code is a broken deployment, not a
    /// caller error, so it throws.
    /// </summary>
    public async Task<int> IdOfAsync(LookupTable table, string code, CancellationToken cancellationToken)
    {
        var codes = (await LoadAsync(cancellationToken))[table];
        return codes.TryGetValue(code, out var id)
            ? id
            : throw new InvalidOperationException($"Lookup code '{code}' is missing from {GridTopologyDbContext.Schema}.{TableName(table)}.");
    }

    /// <summary>Codes by id for every lookup table, for turning the ids in a row into the codes snapshots carry.</summary>
    public async Task<LookupCodes> CodesAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref codes) is { } cached)
            return cached;

        var inverted = new LookupCodes((await LoadAsync(cancellationToken)).ToDictionary(
            t => t.Key,
            t => (IReadOnlyDictionary<int, string>)t.Value.ToDictionary(e => e.Value, e => e.Key)));
        Volatile.Write(ref codes, inverted);
        return inverted;
    }

    public async Task<IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<string, int>>> LoadAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref loaded) is { } cached)
            return cached;

        await loadGate.WaitAsync(cancellationToken);
        try
        {
            if (loaded is not null)
                return loaded;

            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await ReadAllAsync(scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>(), cancellationToken);
            Volatile.Write(ref loaded, result);
            return result;
        }
        finally
        {
            loadGate.Release();
        }
    }

    /// <summary>Every row of every lookup table, read now (no caching). Used by the load and by the readiness check.</summary>
    public static async Task<IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<string, int>>> ReadAllAsync(
        GridTopologyDbContext db, CancellationToken cancellationToken) =>
        new Dictionary<LookupTable, IReadOnlyDictionary<string, int>>
        {
            [LookupTable.TechnologyTypes] = await ReadAsync(db.TechnologyTypes, cancellationToken),
            [LookupTable.InterconnectionTypes] = await ReadAsync(db.InterconnectionTypes, cancellationToken),
            [LookupTable.CustomerSegments] = await ReadAsync(db.CustomerSegments, cancellationToken),
            [LookupTable.GridNodeTypes] = await ReadAsync(db.GridNodeTypes, cancellationToken),
            [LookupTable.AssetLifecycleStates] = await ReadAsync(db.AssetLifecycleStates, cancellationToken),
            [LookupTable.ParticipationStates] = await ReadAsync(db.ParticipationStates, cancellationToken),
            [LookupTable.GridStates] = await ReadAsync(db.GridStateTypes, cancellationToken),
        };

    /// <summary>Known codes absent from <paramref name="lookups"/>, as <c>topology.table.code</c>.</summary>
    public static IReadOnlyList<string> MissingKnownCodes(IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<string, int>> lookups) =>
        KnownCodes
            .SelectMany(k => k.Value.Where(code => !lookups[k.Key].ContainsKey(code))
                .Select(code => $"{GridTopologyDbContext.Schema}.{TableName(k.Key)}.{code}"))
            .ToList();

    private static async Task<IReadOnlyDictionary<string, int>> ReadAsync<T>(DbSet<T> table, CancellationToken cancellationToken)
        where T : LookupEntry =>
        await table.AsNoTracking().ToDictionaryAsync(e => e.Code, e => e.Id, StringComparer.Ordinal, cancellationToken);
}

/// <summary>Lookup codes by id, from <see cref="LookupCatalog.CodesAsync"/>.</summary>
internal sealed class LookupCodes(IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<int, string>> codes)
{
    /// <summary>
    /// The code of row <paramref name="id"/> in <paramref name="table"/>. A row referencing an id the table does not
    /// have is a broken deployment (the foreign keys prevent it), so it throws.
    /// </summary>
    public string Of(LookupTable table, int id) =>
        codes[table].TryGetValue(id, out var code)
            ? code
            : throw new InvalidOperationException($"Lookup id {id} is missing from {GridTopologyDbContext.Schema}.{LookupCatalog.TableName(table)}.");
}
