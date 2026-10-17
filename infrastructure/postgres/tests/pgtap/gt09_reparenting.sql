-- GT-09: load transfer / reparenting (model-validation-scenarios.md)
-- Moving a premise to another transformer is the most common topology edit in distribution operations.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_d_community_microgrid.sql

SELECT plan(5);

CREATE TEMP TABLE gt09_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_transfer;
DO $$ BEGIN PERFORM set_config('rva.change_reason', 'load transfer: relieve FXD-XFMR-1', true); END $$;
UPDATE topology.premises SET neighborhood_transformer_id = pg_temp.transformer_id('FXD-XFMR-2') WHERE premise_code = 'FXD-HOME-05';

SELECT results_eq(
    $$SELECT neighborhood_transformer_id, change_reason::text FROM topology.premises_history WHERE premise_id = pg_temp.premise_id('FXD-HOME-05')$$,
    $$VALUES (pg_temp.transformer_id('FXD-XFMR-1'), 'load transfer: relieve FXD-XFMR-1')$$,
    'GT-09: history records the old parent and why it changed');
SELECT is((SELECT count(*) FROM pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-1', (SELECT before_transfer FROM gt09_t))), 5::bigint,
    'GT-09: before the transfer FXD-XFMR-1 serves five homes');
SELECT is((SELECT count(*) FROM pg_temp.scope_assets('neighborhood_transformer', 'FXD-XFMR-1', clock_timestamp())), 4::bigint,
    'GT-09: after the transfer it serves four');
SELECT is((SELECT transformer_code FROM pg_temp.asset_paths(clock_timestamp()) WHERE asset_code = 'FXD-HOME-05-BESS'), 'FXD-XFMR-2',
    'GT-09: the moved premise''s asset resolves its new constraint path without being edited itself');
SELECT ok(
    (SELECT sum(rated_kva) <= max(transformer_kva) FROM pg_temp.asset_paths(clock_timestamp()) WHERE transformer_code = 'FXD-XFMR-1') IS FALSE,
    'GT-09: FXD-XFMR-1 is still over its rating with four homes (30.4 > 25 kVA)');

SELECT * FROM finish();
ROLLBACK;
