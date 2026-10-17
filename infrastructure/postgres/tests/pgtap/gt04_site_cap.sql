-- GT-04: site cap binds while system and node headroom remain (model-validation-scenarios.md)
-- Computing the applicable ceiling (min along the path) is GridTopology's job; here the DB must expose every limit.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_b_nonexport_warehouse.sql
\ir ../fixtures/fixture_c_large_residential.sql

SELECT plan(8);

SELECT is((SELECT export_limit_kw FROM topology.premises WHERE premise_code = 'FXC-PREM'), 10::double precision,
    'GT-04: the premise carries its 10 kW site export cap');
SELECT is((SELECT sum(rated_p_max_kw) FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXC-PREM')), 34.5::double precision,
    'GT-04: the assets together could export 34.5 kW');
SELECT ok(
    (SELECT bool_and(transformer_kva > 34.5 AND feeder_kva > 34.5 AND substation_kva > 34.5)
     FROM pg_temp.asset_paths(clock_timestamp()) WHERE premise_code = 'FXC-PREM'),
    'GT-04: transformer, feeder, and substation all have headroom, so only the site cap binds');
SELECT is(
    (SELECT min(LEAST(rated_p_max_kw, coalesce(asset_export_limit_kw, 'Infinity'), coalesce(premise_export_limit_kw, 'Infinity')))
     FROM pg_temp.asset_paths(clock_timestamp()) WHERE premise_code = 'FXC-PREM'),
    10::double precision, 'GT-04: the per-asset path exposes the site cap as the binding limit');
SELECT is((SELECT s.internal_code::text FROM topology.premises p JOIN topology.customer_segments s USING (customer_segment_id) WHERE premise_code = 'FXC-PREM'),
    'residential', 'GT-04: installed capacity does not change the customer segment');

SELECT ok((SELECT export_limit_kw = 0 AND export_limit_kw IS NOT NULL FROM topology.premises WHERE premise_code = 'FXB-PREM'),
    'GT-04: Fixture B''s zero export limit is stored as 0 (non-export), not NULL (unknown)');
SELECT throws_ok($$UPDATE topology.premises SET export_limit_kw = -1 WHERE premise_code = 'FXC-PREM'$$,
    '23514', NULL, 'GT-04: a negative site export limit is rejected');
SELECT throws_ok($$UPDATE topology.premises SET service_capacity_kva = 0 WHERE premise_code = 'FXC-PREM'$$,
    '23514', NULL, 'GT-04: a zero service capacity is rejected');

SELECT * FROM finish();
ROLLBACK;
