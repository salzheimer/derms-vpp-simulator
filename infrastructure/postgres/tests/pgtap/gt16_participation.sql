-- GT-16: program participation is separate from lifecycle (model-validation-scenarios.md)
-- A commissioned asset is physically in service; whether the VPP may use it is its participation state.
-- Multi-program enrollment (one asset in several programs) would move this to an enrollment table.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(8);

SELECT lives_ok(
    $$UPDATE topology.assets SET participation_state_id = topology.participation_state_id('not_enrolled') WHERE asset_code = 'FXC-BESS-1'$$,
    'GT-16: an asset can be commissioned but not enrolled');
SELECT ok((SELECT lifecycle = 'commissioned' AND participation = 'not_enrolled' FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXC-BESS-1'),
    'GT-16: ... and both states are visible on its snapshot');

INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva, asset_lifecycle_state_id)
VALUES ('GT16-INSTALLED', pg_temp.premise_id('FXC-PREM'), pg_temp.interconnection('btm'), -5, 5, 5, topology.asset_lifecycle_state_id('installed'));
SELECT throws_ok(
    $$UPDATE topology.assets SET participation_state_id = topology.participation_state_id('active') WHERE asset_code = 'GT16-INSTALLED'$$,
    '23514', NULL, 'GT-16: an asset that is not commissioned cannot be active');
SELECT throws_ok(
    $$UPDATE topology.assets SET participation_state_id = topology.participation_state_id('suspended') WHERE asset_code = 'GT16-INSTALLED'$$,
    '23514', NULL, 'GT-16: an asset that is not commissioned cannot be suspended');

CREATE TEMP TABLE gt16_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_suspension;
DO $$ BEGIN PERFORM set_config('rva.change_reason', 'suspended: failed two performance tests', true); END $$;
UPDATE topology.assets SET participation_state_id = topology.participation_state_id('suspended') WHERE asset_code = 'FXC-BESS-2';
SELECT is((SELECT participation FROM pg_temp.asset_paths((SELECT before_suspension FROM gt16_t)) WHERE asset_code = 'FXC-BESS-2'),
    'active', 'GT-16: participation is versioned: before the suspension the asset was active');
SELECT is((SELECT change_reason::text FROM topology.assets_history WHERE asset_id = pg_temp.asset_id('FXC-BESS-2')),
    'suspended: failed two performance tests', 'GT-16: the suspension reason is recorded');

SELECT throws_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), decommissioned_date = '2026-09-30'
      WHERE asset_code = 'FXC-BESS-3'$$,
    '23514', NULL, 'GT-16: an active asset must leave its program before it is decommissioned');
SELECT lives_ok(
    $$UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), decommissioned_date = '2026-09-30',
             participation_state_id = topology.participation_state_id('opted_out') WHERE asset_code = 'FXC-BESS-3'$$,
    'GT-16: ... and can be decommissioned once it has left');

SELECT * FROM finish();
ROLLBACK;
