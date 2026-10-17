-- GT-01: normal hierarchy and stable node identity (model-validation-scenarios.md)
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(17);

-- every node row has a grid_node_id registered under its own node type
SELECT is(
    (SELECT count(*) FROM (
        SELECT grid_node_id, 'substation' AS t FROM topology.substations
        UNION ALL SELECT grid_node_id, 'feeder' FROM topology.feeders
        UNION ALL SELECT grid_node_id, 'neighborhood_transformer' FROM topology.neighborhood_transformers
        UNION ALL SELECT grid_node_id, 'premise' FROM topology.premises
        UNION ALL SELECT grid_node_id, 'microgrid' FROM topology.microgrids) n
     JOIN topology.grid_nodes g USING (grid_node_id)
     JOIN topology.grid_node_types gt USING (grid_node_type_id)
     WHERE gt.internal_code <> n.t),
    0::bigint, 'GT-01: every node''s grid_node_id is registered under its own node type');

SELECT is(
    (SELECT count(DISTINCT grid_node_id) = count(*) FROM (
        SELECT grid_node_id FROM topology.substations UNION ALL SELECT grid_node_id FROM topology.feeders
        UNION ALL SELECT grid_node_id FROM topology.neighborhood_transformers UNION ALL SELECT grid_node_id FROM topology.premises
        UNION ALL SELECT grid_node_id FROM topology.microgrids) n),
    TRUE, 'GT-01: grid_node_ids are unique across levels');

SELECT is(
    (SELECT count(*) FROM topology.premises p
     JOIN topology.neighborhood_transformers x USING (neighborhood_transformer_id)
     JOIN topology.feeders f ON f.feeder_id = x.feeder_id
     JOIN topology.substations s ON s.substation_id = f.substation_id
     WHERE p.premise_code LIKE 'FXD-HOME-%' AND s.substation_code = 'FXD-SUB'),
    20::bigint, 'GT-01: premise -> transformer -> feeder -> substation chains resolve');

-- identity is assigned by trigger and immutable
SELECT lives_ok(
    $$INSERT INTO topology.substations (substation_code, rated_capacity_kva, grid_node_id) VALUES ('GT01-SUB', 1000, 999999)$$,
    'GT-01: a client-supplied grid_node_id on insert is accepted');
SELECT isnt((SELECT grid_node_id FROM topology.substations WHERE substation_code = 'GT01-SUB'), 999999::bigint,
    'GT-01: ... and replaced by a trigger-assigned id');

UPDATE topology.feeders SET grid_node_id = grid_node_id + 1000 WHERE feeder_code = 'FXD-FDR';
SELECT is((SELECT f.grid_node_id FROM topology.feeders f WHERE feeder_code = 'FXD-FDR'),
          (SELECT g.grid_node_id FROM topology.grid_nodes g
           JOIN topology.grid_node_types gt USING (grid_node_type_id)
           JOIN topology.feeders f ON f.grid_node_id = g.grid_node_id
           WHERE f.feeder_code = 'FXD-FDR' AND gt.internal_code = 'feeder'),
    'GT-01: a node''s grid_node_id cannot be changed');
SELECT is((SELECT count(*) FROM topology.feeders_history h JOIN topology.feeders f USING (feeder_id) WHERE f.feeder_code = 'FXD-FDR'),
    0::bigint, 'GT-01: an attempted grid_node_id change writes no history');

SELECT throws_ok(
    $$DELETE FROM topology.grid_nodes WHERE grid_node_id = (SELECT grid_node_id FROM topology.feeders WHERE feeder_code = 'FXD-FDR')$$,
    'P0001', NULL, 'GT-01: grid nodes cannot be deleted');
SELECT throws_ok(
    $$UPDATE topology.grid_nodes SET grid_node_type_id = grid_node_type_id WHERE grid_node_id = (SELECT grid_node_id FROM topology.feeders WHERE feeder_code = 'FXD-FDR')$$,
    'P0001', NULL, 'GT-01: grid nodes cannot be changed');

CREATE TEMP TABLE gt01_removed ON COMMIT DROP AS SELECT grid_node_id FROM topology.premises WHERE premise_code = 'FXD-OTHER-HOME';
DELETE FROM topology.assets WHERE asset_code = 'FXD-OTHER-HOME-BESS';
DELETE FROM topology.premises WHERE premise_code = 'FXD-OTHER-HOME';
SELECT is((SELECT count(*) FROM topology.grid_nodes WHERE grid_node_id = (SELECT grid_node_id FROM gt01_removed)),
    1::bigint, 'GT-01: a deleted node''s grid_node_id stays registered');

-- invalid hierarchy
SELECT throws_ok(
    $$INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv) VALUES (-1, 'GT01-FDR', 12.47)$$,
    '23503', NULL, 'GT-01: a feeder with a missing parent substation is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.premises (neighborhood_transformer_id, feeder_id, premise_code, customer_segment_id)
      VALUES (pg_temp.transformer_id('FXD-XFMR-1'), pg_temp.feeder_id('FXD-FDR'), 'GT01-PREM', pg_temp.segment('residential'))$$,
    '23514', NULL, 'GT-01: a premise with two service points is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.premises (premise_code, customer_segment_id) VALUES ('GT01-PREM', pg_temp.segment('residential'))$$,
    '23514', NULL, 'GT-01: a premise with no service point is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, premise_id, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT01-BAD', pg_temp.premise_id('FXD-HOME-01'), pg_temp.feeder_id('FXD-FDR'), pg_temp.interconnection('btm'), 0, 5, 5)$$,
    '23514', NULL, 'GT-01: an asset with two connection points is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.assets (asset_code, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
      VALUES ('GT01-BAD', pg_temp.interconnection('ftm'), 0, 5, 5)$$,
    '23514', NULL, 'GT-01: an asset with no connection point is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.microgrids (microgrid_code, feeder_id, substation_id)
      VALUES ('GT01-MG', pg_temp.feeder_id('FXD-FDR'), pg_temp.substation_id('FXD-SUB'))$$,
    '23514', NULL, 'GT-01: a microgrid with two points of common coupling is rejected');
SELECT throws_ok(
    $$INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXD-SUB', 1000)$$,
    '23505', NULL, 'GT-01: node codes are unique within their level');

SELECT * FROM finish();
ROLLBACK;
