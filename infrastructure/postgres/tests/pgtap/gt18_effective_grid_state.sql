-- GT-18: effective grid state of assets and dispatch eligibility (model-validation-scenarios.md)
-- Derivation belongs to GridTopology; pg_temp.asset_grid_states and pg_temp.dispatch_eligible in _helpers.sql are a
-- reference implementation, so these tests are the executable spec for it.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(10);

-- a premise on FXD-FDR outside the microgrid, to tell members from non-members
INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id)
VALUES (pg_temp.transformer_id('FXD-XFMR-4'), 'GT18-NONMEMBER', pg_temp.segment('residential'));
INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva,
                             asset_lifecycle_state_id, participation_state_id, in_service_date)
VALUES ('GT18-NONMEMBER-BESS', pg_temp.premise_id('GT18-NONMEMBER'), pg_temp.interconnection('btm'), -5, 5, 5,
        topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), '2024-06-01');

SELECT is((SELECT count(*) FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE grid_state <> 'connected_energized'), 0::bigint,
    'GT-18: with no events every asset is connected and energized');

DO $$ BEGIN PERFORM pg_temp.record_grid_state('feeder', 'FXD-FDR', 'connected_deenergized'); END $$;
SELECT is((SELECT count(*) FROM pg_temp.asset_grid_states(clock_timestamp()) g JOIN pg_temp.asset_paths(clock_timestamp()) p USING (asset_code)
           WHERE p.feeder_code = 'FXD-FDR' AND g.grid_state <> 'connected_deenergized'), 0::bigint,
    'GT-18: a feeder outage de-energizes every asset below it');
SELECT is((SELECT grid_state FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE asset_code = 'FXD-OTHER-HOME-BESS'),
    'connected_energized', 'GT-18: assets on a sibling feeder stay energized');

DO $$ BEGIN PERFORM pg_temp.record_grid_state('premise', 'FXD-SCHOOL', 'islanded'); END $$;
SELECT set_eq($$SELECT asset_code FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE grid_state = 'islanded'$$,
    ARRAY['FXD-SCHOOL-PV', 'FXD-SCHOOL-BESS'], 'GT-18: an island-capable premise islands its own assets during the outage');

DO $$ BEGIN PERFORM pg_temp.record_grid_state('microgrid', 'FXD-MG', 'islanded'); END $$;
SELECT is((SELECT count(*) FROM pg_temp.asset_grid_states(clock_timestamp()) g JOIN pg_temp.asset_paths(clock_timestamp()) p USING (asset_code)
           WHERE p.microgrid_code = 'FXD-MG' AND g.grid_state <> 'islanded'), 0::bigint,
    'GT-18: when the microgrid islands, every member is islanded');
SELECT is((SELECT grid_state FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE asset_code = 'GT18-NONMEMBER-BESS'),
    'connected_deenergized', 'GT-18: a non-member on the same feeder stays de-energized');

DO $$ BEGIN PERFORM pg_temp.record_grid_state('neighborhood_transformer', 'FXD-XFMR-2', 'connected_deenergized'); END $$;
SELECT is((SELECT count(*) FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE asset_code BETWEEN 'FXD-HOME-06-BESS' AND 'FXD-HOME-10-BESS'
           AND grid_state = 'connected_deenergized'), 5::bigint,
    'GT-18: a transformer fault inside the islanded microgrid de-energizes its homes');

DO $$ BEGIN PERFORM pg_temp.record_grid_state('asset', 'FXD-HOME-01-BESS', 'disconnected'); END $$;
SELECT is((SELECT grid_state FROM pg_temp.asset_grid_states(clock_timestamp()) WHERE asset_code = 'FXD-HOME-01-BESS'),
    'disconnected', 'GT-18: an asset''s own disconnect overrides its path');

-- dispatch eligibility: commissioned, active, energized
UPDATE topology.assets SET participation_state_id = topology.participation_state_id('suspended') WHERE asset_code = 'FXD-HOME-02-BESS';
SELECT ok(
    NOT EXISTS (SELECT 1 FROM pg_temp.dispatch_eligible(clock_timestamp()) e
                WHERE e IN ('FXD-HOME-01-BESS', 'FXD-HOME-02-BESS', 'FXD-HOME-06-BESS', 'GT18-NONMEMBER-BESS')),
    'GT-18: disconnected, suspended, and de-energized assets are not dispatch-eligible');
SELECT ok(
    (SELECT count(*) = 2 FROM pg_temp.dispatch_eligible(clock_timestamp()) e WHERE e IN ('FXD-HOME-03-BESS', 'FXD-OTHER-HOME-BESS')),
    'GT-18: islanded and grid-energized active assets are dispatch-eligible');

SELECT * FROM finish();
ROLLBACK;
