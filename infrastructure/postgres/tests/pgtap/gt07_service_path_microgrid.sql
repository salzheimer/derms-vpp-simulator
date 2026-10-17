-- GT-07: premise service path and microgrid membership (model-validation-scenarios.md)
-- PENDING (domain): cross-feeder microgrid membership is rejected by GridTopology validation (boundary test below).
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_b_nonexport_warehouse.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(8);

SELECT lives_ok(
    $$INSERT INTO topology.premises (feeder_id, premise_code, customer_segment_id)
      VALUES (pg_temp.feeder_id('FXD-FDR'), 'GT07-PRIMARY', pg_temp.segment('commercial_industrial'))$$,
    'GT-07: a primary-service premise connects directly to a feeder');
SELECT is((SELECT transformer_code FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-HOME-01-BESS'),
    'FXD-XFMR-1', 'GT-07: a secondary-service premise connects through its transformer');

SELECT ok((SELECT can_island FROM topology.premises WHERE premise_code = 'FXB-PREM')
          AND NOT EXISTS (SELECT 1 FROM topology.assets WHERE premise_id = pg_temp.premise_id('FXB-PREM') AND microgrid_id IS NOT NULL),
    'GT-07: a single-site BTM microgrid is the premise''s can_island, not a microgrid row');

SELECT throws_ok(
    $$INSERT INTO topology.microgrids (microgrid_code) VALUES ('GT07-MG')$$,
    '23514', NULL, 'GT-07: a microgrid with no point of common coupling is rejected');
SELECT lives_ok(
    $$INSERT INTO topology.microgrids (microgrid_code, substation_id) VALUES ('GT07-MG-SUB', pg_temp.substation_id('FXD-SUB'))$$,
    'GT-07: a microgrid can couple at a substation');

SELECT is((SELECT count(*) FROM pg_temp.asset_paths(clock_timestamp()) WHERE microgrid_code = 'FXD-MG'),
    23::bigint, 'GT-07: FXD-MG has 20 homes, the school''s 2 assets, and the community battery');
SELECT is((SELECT count(*) FROM pg_temp.asset_paths(clock_timestamp()) WHERE microgrid_code = 'FXD-MG' AND feeder_code <> microgrid_feeder_code),
    0::bigint, 'GT-07: every FXD-MG member lies under the microgrid''s own feeder');

SELECT lives_ok(
    $$UPDATE topology.assets SET microgrid_id = pg_temp.microgrid_id('FXD-MG') WHERE asset_code = 'FXD-OTHER-HOME-BESS'$$,
    'GT-07 boundary: the DB accepts a member on another feeder; GridTopology validation must reject it');

SELECT * FROM finish();
ROLLBACK;
