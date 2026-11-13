using System.Globalization;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// Grid state events written straight to <c>topology.grid_state_events</c>, as <c>pg_temp.record_grid_state</c> in the
/// pgTAP helpers does, so the read side (derivation, <c>GetGridState</c>) is tested without the write endpoint.
/// </summary>
internal static class GridStateEventSql
{
    /// <summary>An event on the node at <paramref name="level"/> (a <c>GridNodeLevels</c> code) with this code.</summary>
    public static string Node(string level, string code, string state, DateTimeOffset? occurredAt = null) => $"""
        INSERT INTO topology.grid_state_events (grid_node_id, grid_state_id, occurred_at, reason)
        VALUES ((SELECT grid_node_id FROM topology.{Table(level)} WHERE {Table(level)[..^1]}_code = '{code}'),
                (SELECT grid_state_id FROM topology.grid_states WHERE internal_code = '{state}'),
                {Time(occurredAt)}, 'test: {state} on {level} {code}')
        """;

    /// <summary>An event on the asset with this code.</summary>
    public static string Asset(string code, string state, DateTimeOffset? occurredAt = null) => $"""
        INSERT INTO topology.grid_state_events (asset_id, grid_state_id, occurred_at, reason)
        VALUES ((SELECT asset_id FROM topology.assets WHERE asset_code = '{code}'),
                (SELECT grid_state_id FROM topology.grid_states WHERE internal_code = '{state}'),
                {Time(occurredAt)}, 'test: {state} on asset {code}')
        """;

    private static string Table(string level) => level switch
    {
        "substation" => "substations",
        "feeder" => "feeders",
        "neighborhood_transformer" => "neighborhood_transformers",
        "premise" => "premises",
        "microgrid" => "microgrids",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private static string Time(DateTimeOffset? at) =>
        at is { } t ? $"'{t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}'::timestamptz" : "clock_timestamp()";
}
