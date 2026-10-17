-- SIM-09: simulated window edges (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(5);

-- November 2, 2025: Pacific clocks fall back, so this local calendar day is 25 hours long
CREATE TEMP TABLE sim09 ON COMMIT DROP AS
SELECT pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, NULL,
                       tstzrange('2025-11-02 00:00 America/Los_Angeles'::timestamptz, '2025-11-03 00:00 America/Los_Angeles'::timestamptz, '[)')) AS dst_run,
       pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, NULL, tstzrange('2025-01-01', '2025-01-02', '[)')) AS half_open_run;

SELECT is(
    (SELECT upper(simulated_window) - lower(simulated_window) FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT dst_run FROM sim09)),
    interval '25 hours', 'SIM-09: a window across the fall-back DST change keeps its true 25-hour length');
SELECT ok(
    (SELECT lower_inc(simulated_window) AND NOT upper_inc(simulated_window) FROM simulation.simulation_runs
     WHERE simulation_run_id = (SELECT half_open_run FROM sim09)),
    'SIM-09: a half-open window keeps its bounds, so consecutive windows do not overlap');
SELECT throws_ok(
    $$SELECT pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, NULL, tstzrange('2025-01-02', '2025-01-01'))$$,
    '22000', NULL, 'SIM-09: a window that ends before it starts is rejected');

SELECT throws_ok(
    $$SELECT pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, NULL, tstzrange('2025-01-01', '2025-01-01', '[)'))$$,
    '23514', NULL, 'SIM-09: an empty window is rejected');
SELECT throws_ok(
    $$SELECT pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, NULL, tstzrange(NULL, '2025-01-01'))$$,
    '23514', NULL, 'SIM-09: a window with no start is rejected');

SELECT * FROM finish();
ROLLBACK;
