-- SIM-04: lifecycle event history and immutable inputs (model-validation-scenarios.md)
-- Fixed inputs are enforced by simulation_runs_bookkeeping; transitions, status/timestamp consistency, and failure
-- reasons by simulation.check_run_lifecycle (the SimulationRun entity enforces the same rules first).
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(23);

DO $$ BEGIN PERFORM set_config('rva.change_reason', 'run created', true); END $$;
CREATE TEMP TABLE sim04 ON COMMIT DROP AS
SELECT pg_temp.new_run('premise', 'FXA-PREM', 'assets', NULL, clock_timestamp() - interval '1 minute') AS run,
       pg_temp.new_run('premise', 'FXA-PREM') AS failed_run;

SELECT results_eq(
    $$SELECT s.name::text, e.reason::text, e.actor_id, e.actor_type::text FROM simulation.simulation_run_events e
      JOIN simulation.simulation_run_status_types s USING (status_type_id) WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    $$VALUES ('Pending', 'run created', '00000000-0000-7000-8000-000000000001'::uuid, 'system')$$,
    'SIM-04: creating a run records a Pending event with actor and reason');
SELECT ok((SELECT topology_as_of = created_at FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    'SIM-04: topology_as_of defaults to created_at');

DO $$ BEGIN PERFORM pg_temp.transition((SELECT run FROM sim04), 'Running', 'worker allocated'); END $$;
SELECT is((SELECT count(*) FROM simulation.simulation_run_events WHERE simulation_run_id = (SELECT run FROM sim04) AND reason = 'worker allocated'),
    1::bigint, 'SIM-04: Pending -> Running records one event with its reason');

UPDATE simulation.simulation_runs SET scenario_name = 'renamed' WHERE simulation_run_id = (SELECT run FROM sim04);
SELECT is((SELECT count(*) FROM simulation.simulation_run_events WHERE simulation_run_id = (SELECT run FROM sim04)), 2::bigint,
    'SIM-04: a change that is not a status change records no event');

DO $$ BEGIN PERFORM pg_temp.transition((SELECT run FROM sim04), 'Completed', 'window finished'); END $$;
SELECT results_eq(
    $$SELECT s.name::text FROM simulation.simulation_run_events e JOIN simulation.simulation_run_status_types s USING (status_type_id)
      WHERE simulation_run_id = (SELECT run FROM sim04) ORDER BY simulation_run_event_id$$,
    ARRAY['Pending', 'Running', 'Completed'], 'SIM-04: each status change produces exactly one event, in order');
SELECT ok((SELECT started_at IS NOT NULL AND ended_at >= started_at FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    'SIM-04: a completed run has started_at and ended_at in order');

SELECT throws_ok($$UPDATE simulation.simulation_run_events SET reason = 'tampered' WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    'P0001', NULL, 'SIM-04: run events cannot be updated');
SELECT throws_ok($$DELETE FROM simulation.simulation_run_events WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    'P0001', NULL, 'SIM-04: run events cannot be deleted');
SELECT throws_ok($$DELETE FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    '23503', NULL, 'SIM-04: a run with events cannot be deleted');

-- fixed inputs (enforced by simulation_runs_bookkeeping)
UPDATE simulation.simulation_runs
SET run_kind_id = run_kind_id + 1, topology_as_of = '2000-01-01', initial_state_as_of = NULL, created_at = '2000-01-01'
WHERE simulation_run_id = (SELECT run FROM sim04);
SELECT results_eq(
    $$SELECT run_kind_id, topology_as_of > '2000-01-01', initial_state_as_of IS NOT NULL, created_at > '2000-01-01'
      FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    $$VALUES (pg_temp.run_kind('chaos'), TRUE, TRUE, TRUE)$$,
    'SIM-04: run kind, topology version, initial state, and created_at cannot drift');

-- failed before start
DO $$ BEGIN PERFORM pg_temp.transition((SELECT failed_run FROM sim04), 'Failed', 'no worker available'); END $$;
SELECT results_eq(
    $$SELECT r.started_at IS NULL, r.ended_at IS NOT NULL, e.reason::text FROM simulation.simulation_runs r
      JOIN simulation.simulation_run_events e ON e.simulation_run_id = r.simulation_run_id AND e.status_type_id = pg_temp.run_status('Failed')
      WHERE r.simulation_run_id = (SELECT failed_run FROM sim04)$$,
    $$VALUES (TRUE, TRUE, 'no worker available')$$,
    'SIM-04: a run that fails before starting has no started_at and records its failure time and reason');

-- ---------------------------------------------------------------- fixed inputs: scope at creation, window and configuration while Pending
CREATE TEMP TABLE sim04_scope ON COMMIT DROP AS
SELECT scope_level_id, scope_root_code, detail_level_id, simulated_window, configuration_json
FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04);
UPDATE simulation.simulation_runs
SET scope_level_id = pg_temp.scope_level('substation'), scope_root_code = 'FXA-SUB', detail_level_id = pg_temp.detail_level('hierarchy'),
    simulated_window = tstzrange('2025-01-01', '2025-01-02'), configuration_json = '{"tampered": true}'
WHERE simulation_run_id = (SELECT run FROM sim04);
SELECT is((SELECT scope_level_id FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    (SELECT scope_level_id FROM sim04_scope), 'SIM-04: scope_level_id cannot drift after creation');
SELECT is((SELECT scope_root_code FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    (SELECT scope_root_code FROM sim04_scope), 'SIM-04: scope_root_code cannot drift after creation');
SELECT is((SELECT detail_level_id FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    (SELECT detail_level_id FROM sim04_scope), 'SIM-04: detail_level_id cannot drift after creation');
SELECT is((SELECT simulated_window FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    (SELECT simulated_window FROM sim04_scope), 'SIM-04: simulated_window cannot change once the run has left Pending');
SELECT is((SELECT configuration_json FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT run FROM sim04)),
    (SELECT configuration_json FROM sim04_scope), 'SIM-04: configuration_json cannot change once the run has left Pending');

-- runs created in their own statements: a volatile new_run() in a WHERE clause would run once per scanned row
CREATE TEMP TABLE sim04_fresh ON COMMIT DROP AS
SELECT pg_temp.new_run('premise', 'FXA-PREM') AS a, pg_temp.new_run('premise', 'FXA-PREM') AS b, pg_temp.new_run('premise', 'FXA-PREM') AS c,
       pg_temp.new_run('premise', 'FXA-PREM') AS d;

UPDATE simulation.simulation_runs
SET scope_root_code = 'FXA-SUB', simulated_window = tstzrange('2025-01-01', '2025-01-02'), configuration_json = '{"faults": []}'
WHERE simulation_run_id = (SELECT d FROM sim04_fresh);
SELECT results_eq(
    $$SELECT scope_root_code::text, simulated_window, configuration_json FROM simulation.simulation_runs WHERE simulation_run_id = (SELECT d FROM sim04_fresh)$$,
    $$VALUES ('FXA-PREM', tstzrange('2025-01-01', '2025-01-02'), '{"faults": []}'::jsonb)$$,
    'SIM-04: while Pending, window and configuration can be edited but scope cannot');

-- ---------------------------------------------------------------- transitions and status/timestamp consistency
-- status only: re-stamping started_at would trip the unrelated ck_simulation_runs_ended and pass for the wrong reason
SELECT throws_ok(
    $$UPDATE simulation.simulation_runs SET status_type_id = pg_temp.run_status('Running') WHERE simulation_run_id = (SELECT run FROM sim04)$$,
    '23514', NULL, 'SIM-04: Completed -> Running is rejected');
SELECT throws_ok(
    $$UPDATE simulation.simulation_runs SET status_type_id = pg_temp.run_status('Pending') WHERE simulation_run_id = (SELECT failed_run FROM sim04)$$,
    '23514', NULL, 'SIM-04: Failed -> Pending is rejected');
SELECT throws_ok(
    $$INSERT INTO simulation.simulation_runs (scenario_name, run_kind_id, status_type_id, scope_level_id, scope_root_code, detail_level_id)
      VALUES ('bad', pg_temp.run_kind('chaos'), pg_temp.run_status('Running'), pg_temp.scope_level('premise'), 'FXA-PREM', pg_temp.detail_level('assets'))$$,
    '23514', NULL, 'SIM-04: a run is created Pending');
SELECT throws_ok(
    $$UPDATE simulation.simulation_runs SET started_at = clock_timestamp()
      WHERE simulation_run_id = (SELECT a FROM sim04_fresh)$$,
    '23514', NULL, 'SIM-04: a Pending run cannot have started_at');
SELECT throws_ok(
    $$UPDATE simulation.simulation_runs SET status_type_id = pg_temp.run_status('Running')
      WHERE simulation_run_id = (SELECT b FROM sim04_fresh)$$,
    '23514', NULL, 'SIM-04: a Running run must have started_at');
DO $$ BEGIN PERFORM pg_temp.transition((SELECT c FROM sim04_fresh), 'Running', 'worker allocated'); END $$;
SELECT throws_ok(
    $$UPDATE simulation.simulation_runs SET status_type_id = pg_temp.run_status('Completed')
      WHERE simulation_run_id = (SELECT c FROM sim04_fresh)$$,
    '23514', NULL, 'SIM-04: a Completed run must have ended_at');

SELECT * FROM finish();
ROLLBACK;
