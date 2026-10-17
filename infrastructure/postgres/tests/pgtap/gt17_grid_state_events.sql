-- GT-17: grid connectivity and energization state events (model-validation-scenarios.md)
-- Grid state changes on operational timescales, so it is stored as append-only events keyed by grid node or asset,
-- with the time it happened in the field (occurred_at) and the time RVA learned of it (recorded_at).
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(14);

SELECT results_eq(
    $$SELECT internal_code::text, is_grid_connected, is_energized FROM topology.grid_states ORDER BY internal_code$$,
    $$VALUES ('connected_deenergized', TRUE, FALSE), ('connected_energized', TRUE, TRUE), ('disconnected', FALSE, FALSE), ('islanded', FALSE, TRUE)$$,
    'GT-17: grid states cover connected/islanded/disconnected x energized/de-energized without invalid combinations');

CREATE TEMP TABLE gt17_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_outage;
DO $$ BEGIN PERFORM pg_temp.record_grid_state('feeder', 'FXD-FDR', 'connected_deenergized', clock_timestamp(), 'feeder breaker trip'); END $$;

SELECT results_eq(
    $$SELECT s.internal_code::text, e.reason::text, e.actor_id, e.actor_type::text FROM topology.grid_state_events e
      JOIN topology.grid_states s USING (grid_state_id) WHERE e.grid_node_id = pg_temp.node('feeder', 'FXD-FDR')$$,
    $$VALUES ('connected_deenergized', 'feeder breaker trip', '00000000-0000-7000-8000-000000000001'::uuid, 'system')$$,
    'GT-17: a node event records the state, reason, and actor');

SELECT lives_ok(
    $$INSERT INTO topology.grid_state_events (grid_node_id, grid_state_id, occurred_at, recorded_at)
      VALUES (pg_temp.node('feeder', 'FXD-FDR-2'), pg_temp.grid_state('connected_energized'), clock_timestamp(), '2000-01-01')$$,
    'GT-17: an event with a client-supplied recorded_at is accepted');
SELECT ok((SELECT recorded_at > '2000-01-01' FROM topology.grid_state_events WHERE grid_node_id = pg_temp.node('feeder', 'FXD-FDR-2')),
    'GT-17: ... and recorded_at is set by trigger');

SELECT throws_ok(
    $$INSERT INTO topology.grid_state_events (grid_node_id, asset_id, grid_state_id, occurred_at)
      VALUES (pg_temp.node('feeder', 'FXD-FDR'), pg_temp.asset_id('FXD-COMM-BESS'), pg_temp.grid_state('disconnected'), clock_timestamp())$$,
    '23514', NULL, 'GT-17: an event targets a node or an asset, not both');
SELECT throws_ok(
    $$INSERT INTO topology.grid_state_events (grid_state_id, occurred_at) VALUES (pg_temp.grid_state('disconnected'), clock_timestamp())$$,
    '23514', NULL, 'GT-17: an event must have a target');
SELECT throws_ok(
    $$INSERT INTO topology.grid_state_events (grid_node_id, grid_state_id, occurred_at)
      VALUES (pg_temp.node('feeder', 'FXD-FDR'), pg_temp.grid_state('connected_energized'), clock_timestamp() + interval '1 hour')$$,
    '23514', NULL, 'GT-17: an event cannot happen in the future');
SELECT throws_ok($$UPDATE topology.grid_state_events SET reason = 'tampered'$$, 'P0001', NULL, 'GT-17: grid state events cannot be updated');
SELECT throws_ok($$DELETE FROM topology.grid_state_events$$, 'P0001', NULL, 'GT-17: grid state events cannot be deleted');

-- current state as of a time
SELECT is((SELECT count(*) FROM topology.latest_grid_states((SELECT before_outage FROM gt17_t)) WHERE grid_node_id = pg_temp.node('feeder', 'FXD-FDR')),
    0::bigint, 'GT-17: before any event a node has no recorded state (normal: connected_energized)');
SELECT is((SELECT s.internal_code::text FROM topology.latest_grid_states(clock_timestamp()) g JOIN topology.grid_states s USING (grid_state_id)
           WHERE g.grid_node_id = pg_temp.node('feeder', 'FXD-FDR')),
    'connected_deenergized', 'GT-17: after the trip the feeder is de-energized');
DO $$ BEGIN PERFORM pg_temp.record_grid_state('feeder', 'FXD-FDR', 'connected_energized', clock_timestamp(), 'feeder restored'); END $$;
SELECT is((SELECT s.internal_code::text FROM topology.latest_grid_states(clock_timestamp()) g JOIN topology.grid_states s USING (grid_state_id)
           WHERE g.grid_node_id = pg_temp.node('feeder', 'FXD-FDR')),
    'connected_energized', 'GT-17: after restoration the latest event wins');

-- late-reported event: known only from recorded_at, so earlier as-of reads stay reproducible
CREATE TEMP TABLE gt17_late ON COMMIT DROP AS SELECT clock_timestamp() AS before_report;
DO $$ BEGIN PERFORM pg_temp.record_grid_state('asset', 'FXD-OTHER-HOME-BESS', 'disconnected', (SELECT before_outage FROM gt17_t), 'field crew report: disconnect opened'); END $$;
SELECT is((SELECT count(*) FROM topology.latest_grid_states((SELECT before_report FROM gt17_late)) WHERE asset_id = pg_temp.asset_id('FXD-OTHER-HOME-BESS')),
    0::bigint, 'GT-17: an as-of read before an event was reported does not see it, even if it happened earlier');
SELECT is((SELECT occurred_at FROM topology.latest_grid_states(clock_timestamp()) WHERE asset_id = pg_temp.asset_id('FXD-OTHER-HOME-BESS')),
    (SELECT before_outage FROM gt17_t), 'GT-17: once reported, the event keeps its field time');

SELECT * FROM finish();
ROLLBACK;
