-- GT-12: nested limits (model-validation-scenarios.md)
-- The DB stores each limit where it applies; the effective limit (the minimum along the path) is computed by
-- GridTopology. These tests check that the inputs are all reachable from one path query.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_c_large_residential.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(4);

SELECT lives_ok($$UPDATE topology.assets SET export_limit_kw = 15 WHERE asset_code = 'FXC-BESS-1'$$,
    'GT-12: a per-system export limit above the site cap is accepted (both are recorded as given)');
SELECT is(
    (SELECT LEAST(rated_p_max_kw, asset_export_limit_kw, premise_export_limit_kw) FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXC-BESS-1'),
    10::double precision, 'GT-12: the site cap is the effective limit for that asset');

UPDATE topology.microgrids SET export_limit_kw = 0 WHERE microgrid_code = 'FXD-MG';
SELECT is(
    (SELECT LEAST(rated_p_max_kw, coalesce(asset_export_limit_kw, 'Infinity'), coalesce(premise_export_limit_kw, 'Infinity'), microgrid_export_limit_kw)
     FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-COMM-BESS'),
    0::double precision, 'GT-12: a non-export microgrid PCC limits its 500 kW community battery to zero export');
SELECT is((SELECT count(*) FROM pg_temp.asset_paths(clock_timestamp()) WHERE microgrid_code = 'FXD-MG' AND microgrid_export_limit_kw IS DISTINCT FROM 0),
    0::bigint, 'GT-12: every microgrid member sees the PCC limit on its path');

SELECT * FROM finish();
ROLLBACK;
