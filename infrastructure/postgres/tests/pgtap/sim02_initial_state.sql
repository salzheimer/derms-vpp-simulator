-- SIM-02: synthetic start versus measured initial state (model-validation-scenarios.md)
-- PENDING (integration): telemetry availability at initial_state_as_of is validated by the application path.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(6);

CREATE TEMP TABLE sim02 ON COMMIT DROP AS
SELECT pg_temp.new_run('premise', 'FXC-PREM') AS synthetic_run,
       pg_temp.new_run('premise', 'FXC-PREM', 'assets', NULL, clock_timestamp() - interval '1 hour') AS measured_run;

SELECT is((SELECT initial_state_as_of FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT synthetic_run FROM sim02)),
    NULL::timestamptz, 'SIM-02: NULL initial_state_as_of marks a synthetic start');
SELECT ok((SELECT initial_state_as_of < created_at FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT measured_run FROM sim02)),
    'SIM-02: a measured-state run records the requested past timestamp');

SELECT throws_ok(
    $$SELECT pg_temp.new_run('premise', 'FXC-PREM', 'assets', NULL, clock_timestamp() + interval '1 hour')$$,
    '23514', NULL, 'SIM-02: an initial state later than run creation is rejected');
SELECT throws_ok(
    $$SELECT pg_temp.new_run('premise', 'FXC-PREM', 'assets', clock_timestamp() + interval '1 hour')$$,
    '23514', NULL, 'SIM-02: a topology version later than run creation is rejected');

INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id, state_of_charge_pct)
VALUES ((SELECT synthetic_run FROM sim02), pg_temp.asset_id('FXC-BESS-1'), 35);
SELECT results_eq(
    $$SELECT asset_id, state_of_charge_pct, is_available FROM simulation.simulation_asset_states WHERE simulation_run_id = (SELECT synthetic_run FROM sim02)$$,
    $$VALUES (pg_temp.asset_id('FXC-BESS-1'), 35::double precision, TRUE)$$,
    'SIM-02: an explicit starting state of charge is stored for the specified asset');
SELECT is((SELECT count(*) FROM simulation.simulation_asset_states WHERE simulation_run_id = (SELECT synthetic_run FROM sim02)), 1::bigint,
    'SIM-02: assets without an override have no row (simulator defaults apply)');

SELECT * FROM finish();
ROLLBACK;
