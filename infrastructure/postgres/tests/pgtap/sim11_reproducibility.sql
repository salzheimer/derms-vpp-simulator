-- SIM-11: reproducibility pair (model-validation-scenarios.md)
-- Two runs with the same inputs, created on either side of a topology edit, resolve the same snapshot when both pin
-- the same topology_as_of.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(3);

CREATE TEMP TABLE sim11 ON COMMIT DROP AS SELECT clock_timestamp() AS pinned;
CREATE TEMP TABLE sim11_first ON COMMIT DROP AS SELECT pg_temp.new_run('feeder', 'FXD-FDR', 'assets', (SELECT pinned FROM sim11)) AS run;

UPDATE topology.neighborhood_transformers SET rated_capacity_kva = 50 WHERE neighborhood_transformer_code = 'FXD-XFMR-1';
UPDATE topology.premises SET neighborhood_transformer_id = pg_temp.transformer_id('FXD-XFMR-2') WHERE premise_code = 'FXD-HOME-01';

CREATE TEMP TABLE sim11_second ON COMMIT DROP AS SELECT pg_temp.new_run('feeder', 'FXD-FDR', 'assets', (SELECT pinned FROM sim11)) AS run;
CREATE TEMP TABLE sim11_runs ON COMMIT DROP AS SELECT f.run AS first_run, s.run AS second_run FROM sim11_first f, sim11_second s;

SELECT ok((SELECT r2.created_at > r1.created_at FROM sim11_runs s
           JOIN simulation.simulation_runs r1 ON r1.simulation_run_id = s.first_run
           JOIN simulation.simulation_runs r2 ON r2.simulation_run_id = s.second_run),
    'SIM-11: the second run was created after the topology edits');
SELECT set_eq(
    $$SELECT p.* FROM sim11_runs s JOIN simulation.simulation_runs r ON r.simulation_run_id = s.first_run,
             pg_temp.asset_paths(r.topology_as_of) p WHERE p.feeder_code = r.scope_root_code$$,
    $$SELECT p.* FROM sim11_runs s JOIN simulation.simulation_runs r ON r.simulation_run_id = s.second_run,
             pg_temp.asset_paths(r.topology_as_of) p WHERE p.feeder_code = r.scope_root_code$$,
    'SIM-11: both runs resolve identical scoped topology');
SELECT is((SELECT transformer_kva FROM sim11_runs s JOIN simulation.simulation_runs r ON r.simulation_run_id = s.second_run,
                  pg_temp.asset_paths(r.topology_as_of) p WHERE p.asset_code = 'FXD-HOME-01-BESS'),
    25::double precision, 'SIM-11: the later run sees the pre-edit transformer rating and parent');

SELECT * FROM finish();
ROLLBACK;
