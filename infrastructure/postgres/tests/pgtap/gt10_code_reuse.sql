-- GT-10: decommission vs correction, and code reuse (model-validation-scenarios.md)
-- A decommissioned asset keeps its row, so its code stays reserved. A record deleted as a correction never described a
-- real asset, so its code may be used again.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(4);

UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), decommissioned_date = '2026-09-30',
                           participation_state_id = topology.participation_state_id('opted_out')
WHERE asset_code = 'FXC-BESS-3';
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('FXC-BESS-3', pg_temp.premise_id('FXC-PREM'), pg_temp.interconnection('btm'), -5, 5, 5)$$,
    '23505', NULL, 'GT-10: a decommissioned asset''s code cannot be reused');

CREATE TEMP TABLE gt10 ON COMMIT DROP AS SELECT pg_temp.asset_id('FXC-BESS-2') AS old_id;
DO $$ BEGIN PERFORM set_config('rva.change_reason', 'correction: duplicate registration', true); END $$;
DELETE FROM topology.assets WHERE asset_code = 'FXC-BESS-2';
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('FXC-BESS-2', pg_temp.premise_id('FXC-PREM'), pg_temp.interconnection('btm'), -5, 5, 5)$$,
    'GT-10: the code of a record deleted as a correction can be reused');
SELECT isnt(pg_temp.asset_id('FXC-BESS-2'), (SELECT old_id FROM gt10), 'GT-10: the new asset has a new asset_id');
SELECT is(
    (SELECT count(DISTINCT asset_id) FROM (SELECT asset_id FROM topology.assets WHERE asset_code = 'FXC-BESS-2'
                                           UNION ALL SELECT asset_id FROM topology.assets_history WHERE asset_code = 'FXC-BESS-2') x),
    2::bigint, 'GT-10: across history a corrected code can name two asset ids, so history and telemetry key on asset_id');

SELECT * FROM finish();
ROLLBACK;
