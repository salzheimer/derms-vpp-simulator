-- GT-13: microgrid point-of-common-coupling flags and limits (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(4);

SELECT lives_ok(
    $$INSERT INTO topology.microgrids (microgrid_code, feeder_id, can_island, import_limit_kw, export_limit_kw)
      VALUES ('GT13-NOISLAND', pg_temp.feeder_id('FXD-FDR-2'), FALSE, 0, 0)$$,
    'GT-13: a grid-tied (non-islanding) microgrid with zero import and export is representable');
SELECT results_eq(
    $$SELECT can_island, import_limit_kw, export_limit_kw FROM topology.microgrids WHERE microgrid_code = 'GT13-NOISLAND'$$,
    $$VALUES (FALSE, 0::double precision, 0::double precision)$$,
    'GT-13: zero limits are stored as 0, not NULL');
SELECT throws_ok($$UPDATE topology.microgrids SET import_limit_kw = -1 WHERE microgrid_code = 'FXD-MG'$$,
    '23514', NULL, 'GT-13: a negative import limit is rejected');
SELECT throws_ok($$UPDATE topology.microgrids SET export_limit_kw = -1 WHERE microgrid_code = 'FXD-MG'$$,
    '23514', NULL, 'GT-13: a negative export limit is rejected');

SELECT * FROM finish();
ROLLBACK;
