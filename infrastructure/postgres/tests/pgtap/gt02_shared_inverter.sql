-- GT-02: one control interface, multiple technologies, shared rating (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(10);

SELECT is((SELECT count(*) FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXA-PREM')),
    1::bigint, 'GT-02: battery + PV behind one inverter is one asset, not one per technology');

SELECT bag_eq(
    $$SELECT t.internal_code::text FROM topology.asset_components c JOIN topology.technology_types t USING (technology_type_id)
      WHERE c.asset_id = pg_temp.asset_id('FXA-ASSET')$$,
    ARRAY['bess', 'solar_pv'], 'GT-02: the asset keeps a component per technology');

SELECT ok(
    (SELECT sum(rated_kw) FROM topology.asset_components WHERE asset_id = pg_temp.asset_id('FXA-ASSET'))
        > (SELECT rated_kva FROM topology.assets WHERE asset_code = 'FXA-ASSET'),
    'GT-02: component capacities (20 kW) are kept even though they exceed the asset rating (7.6 kVA)');

SELECT results_eq(
    $$SELECT rated_p_min_kw, rated_p_max_kw, rated_kva FROM topology.assets WHERE asset_code = 'FXA-ASSET'$$,
    $$VALUES (-7.6::double precision, 7.6::double precision, 7.6::double precision)$$,
    'GT-02: the asset carries its own grid-facing bounds and kVA rating');

SELECT is((SELECT rated_kwh FROM topology.asset_components c WHERE asset_id = pg_temp.asset_id('FXA-ASSET') AND technology_type_id = pg_temp.technology('bess')),
    20::double precision, 'GT-02: storage energy lives on the battery component');

SELECT throws_ok(
    $$INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id)
      VALUES (pg_temp.asset_id('FXA-ASSET'), pg_temp.technology('solar_pv'), pg_temp.profile('fxa_battery_5kw_10kwh'))$$,
    '23503', NULL, 'GT-02: a PV component pointing at a battery profile is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id)
      VALUES (pg_temp.asset_id('FXA-ASSET'), pg_temp.technology('bess'), pg_temp.profile('fxa_hybrid_inverter_7_6'))$$,
    '23503', NULL, 'GT-02: a component pointing at control-only equipment is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.asset_components (asset_id, technology_type_id, unit_count) VALUES (pg_temp.asset_id('FXA-ASSET'), pg_temp.technology('bess'), 0)$$,
    '23514', NULL, 'GT-02: a component with zero units is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT02-BAD', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), 0, 20, 10)$$,
    '23514', NULL, 'GT-02: an asset whose power exceeds its kVA rating is rejected');

CREATE TEMP TABLE gt02_asset ON COMMIT DROP AS SELECT pg_temp.asset_id('FXA-ASSET') AS asset_id;
DELETE FROM topology.assets WHERE asset_code = 'FXA-ASSET';
SELECT is((SELECT count(*) FROM topology.asset_components_history WHERE asset_id = (SELECT asset_id FROM gt02_asset) AND operation = 'DELETE'),
    2::bigint, 'GT-02: deleting the asset removes its components and archives each one');

SELECT * FROM finish();
ROLLBACK;
