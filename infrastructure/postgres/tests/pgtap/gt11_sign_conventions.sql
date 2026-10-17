-- GT-11: sign conventions by technology (model-validation-scenarios.md)
-- Positive = export/discharge, negative = import/charge. A flexible load (p_max = 0) is still dispatchable capability.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(6);

SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT11-PV', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), 0, 7.6, 7.6)$$,
    'GT-11: a PV-only asset exports only (p_min = 0)');
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT11-EVSE', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), -11.5, 0, 11.5)$$,
    'GT-11: a managed EV charger imports only (p_max = 0)');
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT11-V2G', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), -11.5, 9.6, 11.5)$$,
    'GT-11: a bidirectional (V2G) charger can have asymmetric bounds');
SELECT ok((SELECT rated_p_min_kw < 0 AND rated_p_max_kw = 0 FROM topology.assets WHERE asset_code = 'GT11-EVSE'),
    'GT-11: the flexible load keeps a non-zero range, distinct from an asset with no capability');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT11-BAD', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), 5, -5, 5)$$,
    '23514', NULL, 'GT-11: bounds with min above max (a flipped sign convention) are rejected');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT11-BAD', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), -12, 0, 11.5)$$,
    '23514', NULL, 'GT-11: a charging magnitude above the kVA rating is rejected');

SELECT * FROM finish();
ROLLBACK;
