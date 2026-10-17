-- GT-06: connection-point and interconnection consistency (model-validation-scenarios.md)
-- PENDING (domain): non-FTM assets above the premise meter are rejected by GridTopology validation, not the database.
-- The "boundary" test below pins that the DB accepts them, so moving the rule into the DDL is a visible decision.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(8);

SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-PREM-BTM', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('btm'), -5, 5, 5),
             ('GT06-PREM-FTM', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('ftm'), -5, 5, 5),
             ('GT06-PREM-ATM', pg_temp.premise_id('FXA-PREM'), pg_temp.interconnection('atm'), -5, 5, 5)$$,
    'GT-06: a premise-connected asset may be BTM, FTM, or ATM');
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, neighborhood_transformer_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-XFMR', pg_temp.transformer_id('FXA-XFMR'), pg_temp.interconnection('ftm'), -25, 25, 25)$$,
    'GT-06: an FTM asset can connect at a transformer secondary');
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-FDR', pg_temp.feeder_id('FXA-FDR'), pg_temp.interconnection('ftm'), -500, 500, 500)$$,
    'GT-06: an FTM asset can connect at a feeder');
SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, substation_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-SUB', pg_temp.substation_id('FXA-SUB'), pg_temp.interconnection('ftm'), -2000, 2000, 2000)$$,
    'GT-06: an FTM asset can connect at a substation');

SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, neighborhood_transformer_id, feeder_id, substation_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-BAD', pg_temp.premise_id('FXA-PREM'), pg_temp.transformer_id('FXA-XFMR'), pg_temp.feeder_id('FXA-FDR'), pg_temp.substation_id('FXA-SUB'),
              pg_temp.interconnection('ftm'), 0, 5, 5)$$,
    '23514', NULL, 'GT-06: an asset with every connection point set is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, neighborhood_transformer_id, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-BAD', pg_temp.transformer_id('FXA-XFMR'), pg_temp.feeder_id('FXA-FDR'), pg_temp.interconnection('ftm'), 0, 5, 5)$$,
    '23514', NULL, 'GT-06: an asset at both a transformer and a feeder is rejected');

SELECT lives_ok(
    $$INSERT INTO topology.assets (asset_code, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT06-FDR-BTM', pg_temp.feeder_id('FXA-FDR'), pg_temp.interconnection('btm'), 0, 5, 5)$$,
    'GT-06 boundary: the DB accepts a BTM asset at a feeder; GridTopology validation must reject it');

SELECT ok((SELECT can_island FROM topology.premises WHERE premise_code = 'FXA-PREM')
          AND (SELECT it.internal_code = 'btm' FROM topology.assets a JOIN topology.interconnection_types it USING (interconnection_type_id) WHERE asset_code = 'FXA-ASSET'),
    'GT-06: an island-capable premise still records its asset''s interconnection type explicitly');

SELECT * FROM finish();
ROLLBACK;
