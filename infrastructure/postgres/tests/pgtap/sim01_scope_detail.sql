-- SIM-01: run scope and detail level combinations (model-validation-scenarios.md)
-- PENDING (domain): unknown scope_root_code and a root code at the wrong level are rejected through
-- IGridTopologyQueries at run creation (no cross-module FK); the boundary test pins that the DB accepts them.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(9);

SELECT lives_ok(
    $$SELECT pg_temp.new_run(l, c, d) FROM (VALUES ('substation', 'FXD-SUB'), ('feeder', 'FXD-FDR'),
                                                    ('neighborhood_transformer', 'FXD-XFMR-1'), ('premise', 'FXD-HOME-01')) s(l, c)
      CROSS JOIN (VALUES ('hierarchy'), ('assets'), ('assets_with_components')) d(d)$$,
    'SIM-01: runs can be scoped at every level with every detail level');
SELECT is((SELECT count(*) FROM simulation.simulation_runs WHERE scenario_name LIKE 'pgTAP FXD-%'), 12::bigint,
    'SIM-01: ... 4 levels x 3 detail levels');

SELECT throws_ok(
    $$INSERT INTO simulation.simulation_runs (scenario_name, run_kind_id, status_type_id, scope_level_id, scope_root_code, detail_level_id)
      VALUES ('bad', pg_temp.run_kind('chaos'), pg_temp.run_status('Pending'), -1, 'FXD-SUB', pg_temp.detail_level('assets'))$$,
    '23503', NULL, 'SIM-01: an unknown scope level fails run creation');
SELECT throws_ok(
    $$INSERT INTO simulation.simulation_runs (scenario_name, run_kind_id, status_type_id, scope_level_id, scope_root_code, detail_level_id)
      VALUES ('bad', pg_temp.run_kind('chaos'), pg_temp.run_status('Pending'), pg_temp.scope_level('feeder'), 'FXD-FDR', -1)$$,
    '23503', NULL, 'SIM-01: an unsupported detail level fails run creation');

-- the scoped subtree resolves through the _as_of functions at the run's topology version
SELECT set_eq(
    $$SELECT pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-1', clock_timestamp())$$,
    ARRAY['FXD-HOME-01-BESS', 'FXD-HOME-02-BESS', 'FXD-HOME-03-BESS', 'FXD-HOME-04-BESS', 'FXD-HOME-05-BESS'],
    'SIM-01: a transformer scope contains exactly the assets below it');
SELECT is((SELECT count(*) FROM pg_temp.scope_assets('feeder', 'FXD-FDR', clock_timestamp())), 23::bigint,
    'SIM-01: a feeder scope contains premise- and feeder-connected assets on that feeder');
SELECT ok(NOT EXISTS (SELECT 1 FROM pg_temp.scope_assets('feeder', 'FXD-FDR', clock_timestamp()) a WHERE a = 'FXD-OTHER-HOME-BESS'),
    'SIM-01: a feeder scope does not expand to sibling feeders');
SELECT set_eq($$SELECT pg_temp.scope_assets('premise', 'FXD-SCHOOL', clock_timestamp())$$, ARRAY['FXD-SCHOOL-BESS', 'FXD-SCHOOL-PV'],
    'SIM-01: a premise scope contains only that premise''s assets');

SELECT lives_ok($$SELECT pg_temp.new_run('feeder', 'NO-SUCH-FEEDER')$$,
    'SIM-01 boundary: the DB accepts an unknown root code; run creation must validate it through GridTopology');

SELECT * FROM finish();
ROLLBACK;
