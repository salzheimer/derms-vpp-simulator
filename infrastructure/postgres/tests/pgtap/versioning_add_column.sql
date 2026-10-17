-- Versioned tables can gain columns (PHASE_1_PLAN.md, Digital twin readiness): history archives by column name, and
-- enable_versioning must be re-run after ALTER TABLE. Ported from the former rva_core_smoke_test.sql.
BEGIN;
\ir ../fixtures/_helpers.sql
\ir ../fixtures/fixture_a_hybrid_home.sql

SELECT plan(5);

ALTER TABLE topology.substations ADD COLUMN pgtap_note VARCHAR(50);

SELECT throws_ok(
    $$UPDATE topology.substations SET rated_capacity_kva = 31000 WHERE substation_code = 'FXA-SUB'$$,
    '42703', NULL, 'an update fails loudly when enable_versioning was not re-run after ALTER TABLE');

COMMENT ON COLUMN topology.substations.pgtap_note IS 'pgTAP note. [VARCHAR(50); NULL]';
DO $$ BEGIN PERFORM topology.enable_versioning('substations'); END $$;

SELECT is(
    col_description('topology.substations_history'::regclass,
                    (SELECT attnum FROM pg_attribute WHERE attrelid = 'topology.substations_history'::regclass AND attname = 'pgtap_note')),
    'Archived value of topology.substations.pgtap_note: pgTAP note. [VARCHAR(50); NULL]',
    'enable_versioning copies an added column''s comment to the history table');

CREATE TEMP TABLE va_t ON COMMIT DROP AS SELECT clock_timestamp() AS before_change;
-- dynamic SQL: the column did not exist when this file was parsed by the server-side functions it calls
DO $$ BEGIN EXECUTE $q$UPDATE topology.substations SET pgtap_note = 'uprated', rated_capacity_kva = 32000 WHERE substation_code = 'FXA-SUB'$q$; END $$;

SELECT results_eq(
    $$SELECT operation::text, rated_capacity_kva, pgtap_note::text FROM topology.substations_history WHERE substation_id = pg_temp.substation_id('FXA-SUB')$$,
    $$VALUES ('UPDATE', 30000::double precision, NULL::text)$$,
    'after enable_versioning, history archives a table with an added column by name');
SELECT results_eq(
    $$SELECT rated_capacity_kva, pgtap_note::text FROM topology.substations_as_of((SELECT before_change FROM va_t)) WHERE substation_code = 'FXA-SUB'$$,
    $$VALUES (30000::double precision, NULL::text)$$,
    'as_of works after a column is added');
SELECT is((SELECT pgtap_note::text FROM topology.substations_as_of(clock_timestamp()) WHERE substation_code = 'FXA-SUB'),
    'uprated', 'as_of(now) returns the added column');

SELECT * FROM finish();
ROLLBACK;
