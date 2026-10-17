-- SIM-03: per-asset override bounds and identity (model-validation-scenarios.md)
-- PENDING (domain): unknown or out-of-scope asset ids are rejected through GridTopology contract validation
-- (asset_id deliberately has no cross-schema FK); the boundary test pins that the DB accepts them.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(11);

CREATE TEMP TABLE sim03 ON COMMIT DROP AS SELECT pg_temp.new_run('premise', 'FXC-PREM') AS run;

SELECT lives_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id, is_available, p_min_kw, p_max_kw, state_of_charge_pct)
      VALUES ((SELECT run FROM sim03), pg_temp.asset_id('FXC-BESS-1'), TRUE, -5, 5, 0),
             ((SELECT run FROM sim03), pg_temp.asset_id('FXC-BESS-2'), TRUE, NULL, NULL, 57.5),
             ((SELECT run FROM sim03), pg_temp.asset_id('FXC-BESS-3'), FALSE, NULL, NULL, 100)$$,
    'SIM-03: overrides at 0%, an interior value, and 100%, narrowed bounds, and unavailability are accepted');

SELECT throws_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id) VALUES ((SELECT run FROM sim03), pg_temp.asset_id('FXC-BESS-1'))$$,
    '23505', NULL, 'SIM-03: a second override for the same asset in a run is rejected');
SELECT throws_ok(
    $$UPDATE simulation.simulation_asset_states SET p_min_kw = 6, p_max_kw = 5 WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    '23514', NULL, 'SIM-03: inverted power bounds are rejected');
SELECT throws_ok(
    $$UPDATE simulation.simulation_asset_states SET state_of_charge_pct = -0.1 WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    '23514', NULL, 'SIM-03: state of charge below 0% is rejected');
SELECT throws_ok(
    $$UPDATE simulation.simulation_asset_states SET state_of_charge_pct = 100.1 WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    '23514', NULL, 'SIM-03: state of charge above 100% is rejected');
SELECT throws_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id) VALUES ('00000000-0000-7000-8000-00000000dead', pg_temp.asset_id('FXC-BESS-1'))$$,
    'P0001', NULL, 'SIM-03: an override for an unknown run is rejected (by the Pending-only trigger, which fires before the FK)');

SELECT lives_ok(
    $$UPDATE simulation.simulation_asset_states SET state_of_charge_pct = 20 WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    'SIM-03: overrides can change while the run is Pending');
SELECT lives_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id) VALUES ((SELECT run FROM sim03), -42)$$,
    'SIM-03 boundary: the DB accepts an unknown asset id; GridTopology contract validation must reject it');

DO $$ BEGIN PERFORM pg_temp.transition((SELECT run FROM sim03), 'Running', 'worker allocated'); END $$;

SELECT throws_ok(
    $$INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id) VALUES ((SELECT run FROM sim03), -43)$$,
    'P0001', NULL, 'SIM-03: overrides cannot be added once the run is Running');
SELECT throws_ok(
    $$UPDATE simulation.simulation_asset_states SET state_of_charge_pct = 50 WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    'P0001', NULL, 'SIM-03: overrides cannot be changed once the run is Running');
SELECT throws_ok(
    $$DELETE FROM simulation.simulation_asset_states WHERE asset_id = pg_temp.asset_id('FXC-BESS-1')$$,
    'P0001', NULL, 'SIM-03: overrides cannot be removed once the run is Running');

SELECT * FROM finish();
ROLLBACK;
