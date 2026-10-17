-- SIM-05: simulation time window and topology cutoff (model-validation-scenarios.md)
-- Window edge cases (empty, DST, half-open) are in sim09_window_edges.sql.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(5);

CREATE TEMP TABLE sim05_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_update;
UPDATE topology.assets SET rated_p_max_kw = 5 WHERE asset_code = 'FXC-BESS-1';

CREATE TEMP TABLE sim05 ON COMMIT DROP AS
SELECT pg_temp.new_run('premise', 'FXC-PREM', 'assets', (SELECT before_update FROM sim05_t), NULL,
                       tstzrange('2024-01-12 00:00-08', '2024-01-15 00:00-08', '[)')) AS bounded_run,
       pg_temp.new_run('premise', 'FXC-PREM', 'assets', NULL, NULL, tstzrange('2024-01-12 00:00-08', NULL, '[)')) AS unbounded_run;

SELECT is(
    (SELECT a.rated_p_max_kw FROM simulation.simulation_runs r, topology.assets_as_of(r.topology_as_of) a
     WHERE r.simulation_run_id = (SELECT bounded_run FROM sim05) AND a.asset_code = 'FXC-BESS-1'),
    11.5::double precision, 'SIM-05: a run pinned before an asset update reads the asset as it was');
SELECT is(
    (SELECT a.rated_p_max_kw FROM simulation.simulation_runs r, topology.assets_as_of(r.topology_as_of) a
     WHERE r.simulation_run_id = (SELECT unbounded_run FROM sim05) AND a.asset_code = 'FXC-BESS-1'),
    5::double precision, 'SIM-05: a run created after the update reads the updated asset');
SELECT ok(
    (SELECT upper(simulated_window) < created_at AND NOT simulated_window @> created_at
     FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT bounded_run FROM sim05)),
    'SIM-05: the simulated window (a replayed January 2024 storm) is independent of when the run executes');
SELECT ok((SELECT upper_inf(simulated_window) FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT unbounded_run FROM sim05)),
    'SIM-05: an unbounded window is stored as open-ended');
CREATE TEMP TABLE sim05_plain ON COMMIT DROP AS SELECT pg_temp.new_run('premise', 'FXC-PREM') AS run;
SELECT is((SELECT simulated_window FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim05_plain)),
    NULL::tstzrange, 'SIM-05: a run need not be tied to a calendar period');

SELECT * FROM finish();
ROLLBACK;
