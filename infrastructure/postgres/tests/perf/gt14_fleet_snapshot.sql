-- GT-14: fleet-scale snapshot (model-validation-scenarios.md). Timing check, run only with run_tests.sh --perf:
-- wall-clock budgets vary by machine, so this is a regression guard, not a gate.
-- Builds 1 substation, 4 feeders, 100 transformers, 600 premises with one asset each, then applies 600 edits so the
-- _as_of functions read through both current and history rows.
BEGIN;
\ir ../fixtures/_helpers.sql

SELECT plan(3);

DO $$
DECLARE
    v_sub bigint;
BEGIN
    INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('PERF-SUB', 50000) RETURNING substation_id INTO v_sub;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    SELECT v_sub, 'PERF-FDR-' || f, 12.47, 8000 FROM generate_series(1, 4) f;
    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    SELECT pg_temp.feeder_id('PERF-FDR-' || (x % 4 + 1)), 'PERF-XFMR-' || x, 50, 240 FROM generate_series(1, 100) x;
    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id)
    SELECT pg_temp.transformer_id('PERF-XFMR-' || (p % 100 + 1)), 'PERF-PREM-' || p, pg_temp.segment('residential') FROM generate_series(1, 600) p;
    INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    SELECT 'PERF-ASSET-' || p, pg_temp.premise_id('PERF-PREM-' || p), pg_temp.interconnection('btm'), -5, 5, 5 FROM generate_series(1, 600) p;
    UPDATE topology.assets SET export_limit_kw = 4 WHERE asset_code LIKE 'PERF-ASSET-%';
END $$;
ANALYZE topology.assets, topology.assets_history, topology.premises, topology.neighborhood_transformers, topology.feeders;

CREATE TEMP TABLE gt14 ON COMMIT DROP AS SELECT clock_timestamp() AS t0;
CREATE TEMP TABLE gt14_snapshot ON COMMIT DROP AS SELECT * FROM pg_temp.asset_paths(clock_timestamp()) WHERE substation_code = 'PERF-SUB';
CREATE TEMP TABLE gt14_elapsed ON COMMIT DROP AS SELECT clock_timestamp() - (SELECT t0 FROM gt14) AS elapsed;

SELECT is((SELECT count(*) FROM gt14_snapshot), 600::bigint, 'GT-14: a substation snapshot contains all 600 assets');
SELECT is((SELECT count(*) FROM gt14_snapshot WHERE transformer_code IS NULL OR feeder_code IS NULL), 0::bigint,
    'GT-14: every asset resolves its full path');
SELECT cmp_ok((SELECT elapsed FROM gt14_elapsed), '<', interval '2 seconds',
    'GT-14: a 600-asset substation snapshot through the _as_of functions completes within 2 s');

SELECT * FROM finish();
ROLLBACK;
