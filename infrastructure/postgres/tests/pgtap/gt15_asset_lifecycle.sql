-- GT-15: asset lifecycle and decommissioning (model-validation-scenarios.md)
-- Lifecycle is planned -> installed -> commissioned -> decommissioned, versioned on the asset. Decommissioning is a
-- state change that keeps the asset; DELETE is reserved for correcting records entered in error.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(19);

-- registration defaults to a state that cannot be dispatched
INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
VALUES ('GT15-NEW', pg_temp.premise_id('FXC-PREM'), pg_temp.interconnection('btm'), -5, 5, 5);
SELECT results_eq(
    $$SELECT lifecycle, participation, in_service_date FROM pg_temp.asset_paths(clock_timestamp()) p
      JOIN topology.assets a USING (asset_id) WHERE p.asset_code = 'GT15-NEW'$$,
    $$VALUES ('planned'::text, 'not_enrolled'::text, NULL::date)$$,
    'GT-15: a new asset defaults to planned and not enrolled');
SELECT throws_ok(
    $$UPDATE topology.assets SET in_service_date = '2025-01-01' WHERE asset_code = 'GT15-NEW'$$,
    '23514', NULL, 'GT-15: a planned asset cannot have an in-service date');

-- forward through the lifecycle
SELECT lives_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('installed') WHERE asset_code = 'GT15-NEW'$$,
    'GT-15: planned -> installed');
SELECT throws_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('commissioned') WHERE asset_code = 'GT15-NEW'$$,
    '23514', NULL, 'GT-15: commissioning requires an in-service date');
SELECT lives_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('commissioned'), in_service_date = '2025-03-15'
      WHERE asset_code = 'GT15-NEW'$$,
    'GT-15: installed -> commissioned with its in-service date');
SELECT bag_eq(
    $$SELECT s.internal_code::text FROM topology.assets_history h JOIN topology.asset_lifecycle_states s USING (asset_lifecycle_state_id)
      WHERE h.asset_id = pg_temp.asset_id('GT15-NEW')$$,
    ARRAY['planned', 'installed'], 'GT-15: every earlier lifecycle state is kept in history');
SELECT throws_ok(
    $$UPDATE topology.assets SET decommissioned_date = '2026-01-01' WHERE asset_code = 'GT15-NEW'$$,
    '23514', NULL, 'GT-15: a commissioned asset cannot have a decommissioned date');

-- decommissioning
SELECT throws_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), participation_state_id = topology.participation_state_id('opted_out')
      WHERE asset_code = 'FXC-BESS-3'$$,
    '23514', NULL, 'GT-15: decommissioning requires a decommissioned date');
SELECT throws_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), participation_state_id = topology.participation_state_id('opted_out'),
             decommissioned_date = '2024-01-01' WHERE asset_code = 'FXC-BESS-3'$$,
    '23514', NULL, 'GT-15: an asset cannot be decommissioned before it entered service');

DO $$ BEGIN PERFORM set_config('rva.change_reason', 'end of life: battery replaced', true); END $$;
CREATE TEMP TABLE gt15_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_decommission;
UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'),
                           participation_state_id = topology.participation_state_id('opted_out'), decommissioned_date = '2026-09-30'
WHERE asset_code = 'FXC-BESS-3';

SELECT results_eq(
    $$SELECT lifecycle, decommissioned_date FROM pg_temp.asset_paths(clock_timestamp()) p JOIN topology.assets a USING (asset_id) WHERE p.asset_code = 'FXC-BESS-3'$$,
    $$VALUES ('decommissioned'::text, DATE '2026-09-30')$$,
    'GT-15: a decommissioned asset stays in the registry with its state and date');
SELECT results_eq(
    $$SELECT operation::text, change_reason::text FROM topology.assets_history WHERE asset_id = pg_temp.asset_id('FXC-BESS-3')$$,
    $$VALUES ('UPDATE', 'end of life: battery replaced')$$,
    'GT-15: decommissioning is recorded as a state change with its reason, distinct from a delete');
SELECT is((SELECT lifecycle FROM pg_temp.asset_paths((SELECT before_decommission FROM gt15_t)) WHERE asset_code = 'FXC-BESS-3'),
    'commissioned', 'GT-15: snapshots before the decommission still show it commissioned');
SELECT ok(NOT EXISTS (SELECT 1 FROM pg_temp.scope_assets('premise', 'FXC-PREM', clock_timestamp()) a WHERE a = 'FXC-BESS-3'),
    'GT-15: a decommissioned asset is no longer part of a run scope');

SELECT throws_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('commissioned'), decommissioned_date = NULL
      WHERE asset_code = 'FXC-BESS-3'$$,
    '23514', NULL, 'GT-15: decommissioned is terminal');
SELECT throws_ok($$DELETE FROM topology.assets WHERE asset_code = 'FXC-BESS-3'$$,
    'P0001', NULL, 'GT-15: a decommissioned asset cannot be deleted (kept for audit and settlement)');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('FXC-BESS-3', pg_temp.premise_id('FXC-PREM'), pg_temp.interconnection('btm'), -5, 5, 5)$$,
    '23505', NULL, 'GT-15: a decommissioned asset''s code stays reserved');

-- corrections
DO $$ BEGIN PERFORM set_config('rva.change_reason', '', true); END $$;
SELECT throws_ok($$DELETE FROM topology.assets WHERE asset_code = 'GT15-NEW'$$,
    'P0001', NULL, 'GT-15: a delete without a change reason is rejected');
DO $$ BEGIN PERFORM set_config('rva.change_reason', 'correction: registered at the wrong premise', true); END $$;
CREATE TEMP TABLE gt15_new ON COMMIT DROP AS SELECT pg_temp.asset_id('GT15-NEW') AS asset_id;
SELECT lives_ok($$DELETE FROM topology.assets WHERE asset_code = 'GT15-NEW'$$,
    'GT-15: a record entered in error can be deleted with a reason');
SELECT results_eq(
    $$SELECT operation::text, change_reason::text FROM topology.assets_history WHERE asset_id = (SELECT asset_id FROM gt15_new) AND operation = 'DELETE'$$,
    $$VALUES ('DELETE', 'correction: registered at the wrong premise')$$,
    'GT-15: the correction is archived as a DELETE with its reason');

SELECT * FROM finish();
ROLLBACK;
