-- SIM-07: as-of override validity (model-validation-scenarios.md)
-- PENDING (domain): GridTopology must validate override asset ids against the run's topology_as_of, not current rows.
-- These tests check that the DB gives it what it needs: an asset decommissioned after the pinned time is still
-- commissioned, and in scope, in the run's as-of snapshot.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(3);

CREATE TEMP TABLE sim07 ON COMMIT DROP AS SELECT clock_timestamp() AS pinned, pg_temp.asset_id('FXC-BESS-3') AS asset_id;
UPDATE topology.assets SET asset_lifecycle_state_id = topology.asset_lifecycle_state_id('decommissioned'), decommissioned_date = '2026-09-30',
                           participation_state_id = topology.participation_state_id('opted_out')
WHERE asset_code = 'FXC-BESS-3';
CREATE TEMP TABLE sim07_run ON COMMIT DROP AS SELECT pg_temp.new_run('premise', 'FXC-PREM', 'assets', (SELECT pinned FROM sim07)) AS run;

SELECT ok(NOT EXISTS (SELECT 1 FROM pg_temp.scope_assets('premise', 'FXC-PREM', clock_timestamp()) a WHERE a = 'FXC-BESS-3'),
    'SIM-07: the decommissioned asset is out of the current scope');
SELECT ok(EXISTS (SELECT 1 FROM simulation.simulation_runs r, pg_temp.scope_assets('premise', 'FXC-PREM', r.topology_as_of) a
                  WHERE r.simulation_run_id = (SELECT run FROM sim07_run) AND a = 'FXC-BESS-3'),
    'SIM-07: ... but is in the scope of a run pinned before the decommission');
SELECT lives_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id, state_of_charge_pct)
      VALUES ((SELECT run FROM sim07_run), (SELECT asset_id FROM sim07), 80)$$,
    'SIM-07 boundary: the DB stores the override (it accepts any asset id); GridTopology must validate it as of topology_as_of');

SELECT * FROM finish();
ROLLBACK;
