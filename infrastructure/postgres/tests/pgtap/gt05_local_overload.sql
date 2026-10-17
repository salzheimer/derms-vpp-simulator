-- GT-05: local transformer overload hidden by aggregate headroom (model-validation-scenarios.md)
-- Static ratings and constraint-path inputs only; power flow is out of Phase 1 scope.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(6);

SELECT ok(
    (SELECT sum(rated_kva) > max(transformer_kva) FROM pg_temp.asset_paths(clock_timestamp()) WHERE transformer_code = 'FXD-XFMR-1'),
    'GT-05: connected capacity under FXD-XFMR-1 (38 kVA) exceeds its 25 kVA rating');
SELECT ok(
    (SELECT sum(rated_kva) < max(feeder_kva) AND sum(rated_kva) < max(substation_kva)
     FROM pg_temp.asset_paths(clock_timestamp()) WHERE feeder_code = 'FXD-FDR'),
    'GT-05: the feeder''s total connected capacity is within feeder and substation ratings');

SELECT results_eq(
    $$SELECT premise_code, transformer_code, transformer_kva, feeder_code, substation_code
      FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-HOME-01-BESS'$$,
    $$VALUES ('FXD-HOME-01'::text, 'FXD-XFMR-1'::text, 25::double precision, 'FXD-FDR'::text, 'FXD-SUB'::text)$$,
    'GT-05: the constraint path of an asset below FXD-XFMR-1 includes the transformer and its rating');
SELECT is(
    (SELECT transformer_code FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-HOME-06-BESS'),
    'FXD-XFMR-2', 'GT-05: an asset elsewhere on the feeder is not constrained by FXD-XFMR-1');
SELECT ok(
    (SELECT transformer_code IS NULL AND feeder_code = 'FXD-FDR' FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-COMM-BESS'),
    'GT-05: a feeder-connected asset''s path starts at the feeder');

SELECT results_eq(
    $$SELECT level, round(sum(rated_kva)::numeric, 3) FROM (
          SELECT 'transformer' AS level, rated_kva FROM pg_temp.asset_paths(clock_timestamp()) WHERE transformer_code = 'FXD-XFMR-1'
          UNION ALL SELECT 'feeder', rated_kva FROM pg_temp.asset_paths(clock_timestamp()) WHERE feeder_code = 'FXD-FDR'
          UNION ALL SELECT 'substation', rated_kva FROM pg_temp.asset_paths(clock_timestamp()) WHERE substation_code = 'FXD-SUB') x
      GROUP BY level ORDER BY level$$,
    $$VALUES ('feeder', 902.000), ('substation', 907.000), ('transformer', 38.000)$$,
    'GT-05: capacity aggregates at transformer, feeder, and substation scope');

SELECT * FROM finish();
ROLLBACK;
