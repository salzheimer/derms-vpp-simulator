-- SIM-10: failure reason and actor on lifecycle events (model-validation-scenarios.md)
-- An audit trail of failures without causes or actors is weak evidence (security.md, NERC CIP).
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(3);

CREATE TEMP TABLE sim10 ON COMMIT DROP AS SELECT pg_temp.new_run('premise', 'FXA-PREM') AS run;
DO $$ BEGIN PERFORM pg_temp.transition((SELECT run FROM sim10), 'Running', 'worker allocated'); END $$;

SELECT is((SELECT actor_id FROM simulation.simulation_run_events WHERE simulation_run_id = (SELECT run FROM sim10) AND status_type_id = pg_temp.run_status('Running')),
    '00000000-0000-7000-8000-000000000001'::uuid, 'SIM-10: a status change records the actor from SET LOCAL rva.actor');

SELECT throws_ok($$SELECT pg_temp.transition((SELECT run FROM sim10), 'Failed', NULL)$$,
    '23514', NULL, 'SIM-10: a run cannot enter Failed without a reason');

SELECT ok(EXISTS (SELECT 1 FROM simulation.simulation_run_events WHERE simulation_run_id = (SELECT run FROM sim10) AND reason = 'worker allocated'),
    'SIM-10: reasons given for earlier transitions are kept');

SELECT * FROM finish();
ROLLBACK;
