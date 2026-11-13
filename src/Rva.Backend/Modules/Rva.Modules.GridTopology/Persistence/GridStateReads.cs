using Microsoft.EntityFrameworkCore;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// The latest grid state of some nodes and one asset, as known at a point in time, through
/// <c>topology.latest_grid_states</c>: events that occurred and were recorded by then, the latest by field time. A
/// target with no row is in its normal state.
/// </summary>
internal static class GridStateReads
{
    /// <summary>The latest states found: node states by grid node id, and the asset's own state (null = no event).</summary>
    public sealed record LatestStates(IReadOnlyDictionary<GridNodeId, string> Nodes, string? Asset);

    /// <summary>
    /// The latest states of <paramref name="nodes"/> and <paramref name="assetId"/> as of <paramref name="asOf"/>; a
    /// current read (null) uses the database clock, the clock that stamps <c>recorded_at</c>.
    /// </summary>
    public static async Task<LatestStates> LatestAsync(
        GridTopologyDbContext db, IReadOnlyCollection<GridNodeId> nodes, AssetId assetId, DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var nodeIds = nodes.Select(n => n.Value).ToArray();
        var asset = assetId.Value;
        var query = asOf is { } at
            ? db.Database.SqlQuery<Row>($"""
                SELECT g.grid_node_id AS "GridNodeId", g.asset_id AS "AssetId", s.internal_code AS "State"
                FROM topology.latest_grid_states({at.ToUniversalTime()}) g
                JOIN topology.grid_states s ON s.grid_state_id = g.grid_state_id
                WHERE g.grid_node_id = ANY({nodeIds}) OR g.asset_id = {asset}
                """)
            : db.Database.SqlQuery<Row>($"""
                SELECT g.grid_node_id AS "GridNodeId", g.asset_id AS "AssetId", s.internal_code AS "State"
                FROM topology.latest_grid_states(clock_timestamp()) g
                JOIN topology.grid_states s ON s.grid_state_id = g.grid_state_id
                WHERE g.grid_node_id = ANY({nodeIds}) OR g.asset_id = {asset}
                """);

        var rows = await query.ToListAsync(cancellationToken);
        return new LatestStates(
            rows.Where(r => r.GridNodeId is not null).ToDictionary(r => new GridNodeId(r.GridNodeId!.Value), r => r.State),
            rows.SingleOrDefault(r => r.AssetId == asset)?.State);
    }

    /// <summary>One row of <c>topology.latest_grid_states</c> with its state code.</summary>
    internal sealed class Row
    {
        public long? GridNodeId { get; init; }
        public long? AssetId { get; init; }
        public string State { get; init; } = "";
    }
}
