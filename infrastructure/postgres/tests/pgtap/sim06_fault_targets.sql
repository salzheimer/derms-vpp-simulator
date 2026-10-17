-- SIM-06: faults at several grid levels (model-validation-scenarios.md)
-- Fault outcomes belong to the Simulator, and configuration_json has no contract yet. These tests check that the DB
-- gives every fault target an unambiguous identity and a well-defined set of affected descendants.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(6);

SELECT is((SELECT count(*) FROM topology.assets WHERE asset_code = 'FXD-HOME-03-BESS'), 1::bigint,
    'SIM-06: an asset-level fault target resolves to exactly one asset by code');
SELECT set_eq($$SELECT pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-1', clock_timestamp())$$,
    $$SELECT asset_code::text FROM topology.assets WHERE asset_code BETWEEN 'FXD-HOME-01-BESS' AND 'FXD-HOME-05-BESS'$$,
    'SIM-06: a transformer fault affects exactly its five homes');
SELECT is((SELECT count(*) FROM pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-1', clock_timestamp()) a
           WHERE a IN (SELECT pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-2', clock_timestamp()))), 0::bigint,
    'SIM-06: a localized fault does not reach sibling transformers');
SELECT is((SELECT count(*) FROM pg_temp.scope_assets('substation', 'FXD-SUB', clock_timestamp())), 24::bigint,
    'SIM-06: a substation-wide outage reaches every descendant on both feeders');

-- Codes are unique per level, not across levels. A fault target named only by code is ambiguous; the JSON contract
-- must carry the level (or grid_node_id).
SELECT lives_ok(
    $$INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
      VALUES (pg_temp.feeder_id('FXD-FDR'), 'FXD-FDR', 25, 240)$$,
    'SIM-06 boundary: a transformer may reuse a feeder''s code, so fault targets need a level or grid_node_id');
SELECT is((SELECT count(DISTINCT grid_node_id) FROM (
              SELECT grid_node_id FROM topology.feeders WHERE feeder_code = 'FXD-FDR'
              UNION ALL SELECT grid_node_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-FDR') n),
    2::bigint, 'SIM-06: ... grid_node_id still tells the two apart');

SELECT * FROM finish();
ROLLBACK;
