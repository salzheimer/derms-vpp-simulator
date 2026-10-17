-- GT-03: separate control interfaces at one premise (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_b_nonexport_warehouse.sql

SELECT plan(4);

SELECT is((SELECT count(*) FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXB-PREM')),
    2::bigint, 'GT-03: independently controlled PV and battery are two assets at one premise');

SELECT is((SELECT count(DISTINCT control_hardware_profile_id) FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXB-PREM')),
    2::bigint, 'GT-03: each asset has its own control equipment');

SELECT results_eq(
    $$SELECT asset_code::text, rated_p_min_kw, rated_p_max_kw FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXB-PREM') ORDER BY asset_code$$,
    $$VALUES ('FXB-BESS', -125::double precision, 125::double precision), ('FXB-PV', 0::double precision, 100::double precision)$$,
    'GT-03: each asset keeps its own limits');

SELECT results_eq(
    $$SELECT DISTINCT premise_code, premise_export_limit_kw FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code IN ('FXB-PV', 'FXB-BESS')$$,
    $$VALUES ('FXB-PREM'::text, 0::double precision)$$,
    'GT-03: both assets resolve the same premise and its site export limit');

SELECT * FROM finish();
ROLLBACK;
