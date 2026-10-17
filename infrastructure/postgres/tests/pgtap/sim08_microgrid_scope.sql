-- SIM-08: microgrid-scoped run (model-validation-scenarios.md)
-- An islanding scenario for Fixture D scopes a run to the microgrid, not to a feeder (which would include non-members).
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(3);

SELECT is((SELECT count(*) FROM pg_temp.scope_assets('microgrid', 'FXD-MG', clock_timestamp())), 23::bigint,
    'SIM-08: the topology can resolve a microgrid''s members as a scope');

SELECT isnt(pg_temp.scope_level('microgrid'), NULL, 'SIM-08: microgrid is a run scope level');
SELECT lives_ok($$SELECT pg_temp.new_run('microgrid', 'FXD-MG')$$, 'SIM-08: a run can be scoped to FXD-MG');

SELECT * FROM finish();
ROLLBACK;
