-- GT-08: historical topology and reproducible snapshot (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(16);

SELECT ok((SELECT created_at IS NOT NULL AND upper_inf(sys_period) FROM topology.assets WHERE asset_code = 'FXA-ASSET'),
    'GT-08: insert stamps created_at and an open sys_period');

-- capture a snapshot before the changes, then change export cap, component rating, and asset placement
CREATE TEMP TABLE gt08_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_change;
CREATE TEMP TABLE gt08_snapshot ON COMMIT DROP AS SELECT * FROM pg_temp.asset_paths((SELECT before_change FROM gt08_t));

DO $$ BEGIN PERFORM set_config('rva.change_reason', 'interconnection amendment', true); END $$;
UPDATE topology.assets SET export_limit_kw = 5 WHERE asset_code = 'FXA-ASSET';
UPDATE topology.asset_components SET rated_kw = 12 WHERE asset_id = pg_temp.asset_id('FXA-ASSET') AND technology_type_id = pg_temp.technology('solar_pv');
INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
VALUES (pg_temp.feeder_id('FXA-FDR'), 'FXA-XFMR-NEW', 75, 240);
UPDATE topology.premises SET neighborhood_transformer_id = pg_temp.transformer_id('FXA-XFMR-NEW') WHERE premise_code = 'FXA-PREM';

SELECT is((SELECT count(*) FROM topology.assets_history WHERE asset_id = pg_temp.asset_id('FXA-ASSET')), 1::bigint,
    'GT-08: an update writes exactly one history row');
SELECT results_eq(
    $$SELECT operation::text, export_limit_kw, changed_by, actor_type::text, change_reason::text, upper_inf(sys_period)
      FROM topology.assets_history WHERE asset_id = pg_temp.asset_id('FXA-ASSET')$$,
    $$VALUES ('UPDATE', NULL::double precision, '00000000-0000-7000-8000-000000000001'::uuid, 'system', 'interconnection amendment', FALSE)$$,
    'GT-08: history holds the previous version with actor, reason, and a closed period');

SELECT is((SELECT export_limit_kw FROM topology.assets_as_of((SELECT before_change FROM gt08_t)) WHERE asset_code = 'FXA-ASSET'),
    NULL::double precision, 'GT-08: assets_as_of(before) returns the old export cap');
SELECT is((SELECT export_limit_kw FROM topology.assets_as_of(clock_timestamp()) WHERE asset_code = 'FXA-ASSET'),
    5::double precision, 'GT-08: assets_as_of(now) returns the new export cap');
SELECT is((SELECT rated_kw FROM topology.asset_components_as_of((SELECT before_change FROM gt08_t))
           WHERE asset_id = pg_temp.asset_id('FXA-ASSET') AND technology_type_id = pg_temp.technology('solar_pv')),
    10::double precision, 'GT-08: asset_components_as_of(before) returns the old component rating');
SELECT is((SELECT transformer_code FROM pg_temp.asset_paths((SELECT before_change FROM gt08_t)) WHERE asset_code = 'FXA-ASSET'),
    'FXA-XFMR', 'GT-08: the earlier snapshot has the earlier parent');
SELECT is((SELECT transformer_code FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXA-ASSET'),
    'FXA-XFMR-NEW', 'GT-08: the later snapshot has the new parent');
SELECT is((SELECT count(*) FROM pg_temp.asset_paths((SELECT before_change FROM gt08_t)) WHERE transformer_code = 'FXA-XFMR-NEW'),
    0::bigint, 'GT-08: a node created after the snapshot time is absent from it');

SELECT set_eq(
    $$SELECT * FROM pg_temp.asset_paths((SELECT before_change FROM gt08_t))$$,
    $$SELECT * FROM gt08_snapshot$$,
    'GT-08: repeating a snapshot for the same asOf after later edits yields the same topology inputs');

SELECT ok((SELECT lower(sys_period) > (SELECT before_change FROM gt08_t) FROM topology.assets WHERE asset_code = 'FXA-ASSET'),
    'GT-08: the current version''s validity start is the time of the change');

-- no-op updates, immutable created_at, deletes
UPDATE topology.assets SET export_limit_kw = 5 WHERE asset_code = 'FXA-ASSET';
SELECT is((SELECT count(*) FROM topology.assets_history WHERE asset_id = pg_temp.asset_id('FXA-ASSET')), 1::bigint,
    'GT-08: a no-op update writes no history');
UPDATE topology.assets SET created_at = '2000-01-01' WHERE asset_code = 'FXA-ASSET';
SELECT ok((SELECT created_at > '2000-01-01' FROM topology.assets WHERE asset_code = 'FXA-ASSET'),
    'GT-08: created_at cannot be changed by the application');

CREATE TEMP TABLE gt08_asset ON COMMIT DROP AS SELECT pg_temp.asset_id('FXA-ASSET') AS asset_id;
CREATE TEMP TABLE gt08_before_delete ON COMMIT DROP AS SELECT clock_timestamp() AS t;
DELETE FROM topology.assets WHERE asset_code = 'FXA-ASSET';
SELECT is((SELECT count(*) FROM topology.assets_history WHERE asset_id = (SELECT asset_id FROM gt08_asset) AND operation = 'DELETE'),
    1::bigint, 'GT-08: a delete archives the removed row');
SELECT is((SELECT count(*) FROM topology.assets_as_of((SELECT t FROM gt08_before_delete)) WHERE asset_code = 'FXA-ASSET'),
    1::bigint, 'GT-08: a deleted asset is still in snapshots taken before the delete');

SELECT hasnt_table('topology', 'grid_nodes_history', 'GT-08: grid node identity is not versioned topology history');

SELECT * FROM finish();
ROLLBACK;
