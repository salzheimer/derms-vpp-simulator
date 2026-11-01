/*
Development topology seed for rva_core (plan p1-s01b, D12): a sample Portland-area grid for local development and
manual testing. Run by seed.sh, inside the one transaction it opens, after catalog.sql. Never loaded by tests, which
use the fixtures in infrastructure/postgres/tests/fixtures/ (A-D) instead. Synthetic: codes, ratings, and locations
are invented and do not describe any real utility's equipment.

  PDX-SUB-ARBOR (30 MVA) and PDX-SUB-CEDAR (25 MVA), each with
  ├── 3 feeders PDX-<ARB|CED>-F1..F3 (12.47 kV, 8 MVA), each with
  │   ├── 10 neighborhood transformers -X01..X10 (150 kVA), each serving 8 homes -H1..H8, one asset -A1 per home
  │   │     in four repeating designs, plus an EV charger -A2 on every tenth home
  │   ├── a pole-mount utility battery on transformer X05 (-X05-PB, transformer-connected, FTM)
  │   ├── a primary-service C&I premise -CI with separate PV (-CI-PV) and battery (-CI-BESS) assets
  │   └── a community battery -CB (feeder-connected, FTM)
  └── a substation battery PDX-SUB-<name>-BESS (substation-connected, FTM)

  Home designs, by home number n (1..480) mod 4: 0 Tesla Powerwall 3 + PV (DC-coupled, can island), 1 Enphase IQ
  batteries + PV behind one IQ Gateway (can island), 2 SolarEdge PV only, 3 Enphase IQ battery only.
  Exceptions that exercise later features:
  * PDX-ARB-F1-X01 is rated 50 kVA, below the ~66 kVA of inverters behind it: the one overloaded transformer.
  * Microgrid PDX-MG-CED-F1 on feeder PDX-CED-F1: the assets of the homes on transformers X01..X03 and the feeder's
    community battery are its members.
  * PDX-ARB-F2-CI is non-export (export limit 0); every 16th home has a 10 kW site export cap.
  * A deterministic mix of lifecycle and participation states (mostly commissioned and active).
  Totals: 2 substations, 6 feeders, 60 transformers, 486 premises, 1 microgrid, 554 assets, 794 components.

Idempotent. Rows are keyed on their natural codes and inserted in their final state (never updated), so a re-run adds
no rows and no history. Node tables use INSERT ... WHERE NOT EXISTS, not ON CONFLICT DO NOTHING: their grid node
trigger is BEFORE INSERT and registers a topology.grid_nodes row before a conflict is detected, so ON CONFLICT would
leave an orphan registry row on every re-run. Components have no natural key, so they are inserted only with the asset
row they belong to, in the same statement.
*/

DO $$
BEGIN
    PERFORM set_config('rva.actor', '019a3c00-0000-7000-8000-0000000000a5', true);   -- the development seed, a system actor
    PERFORM set_config('rva.actor_type', 'system', true);
    PERFORM set_config('rva.change_reason', 'development seed: topology', true);
END $$;

-- ---------------------------------------------------------------- the grid, as temp tables
CREATE TEMP TABLE seed_substations ON COMMIT DROP AS
SELECT * FROM (VALUES
    (1, 'PDX-SUB-ARBOR', 'ARB', 30000.0, -122.600, 45.495),
    (2, 'PDX-SUB-CEDAR', 'CED', 25000.0, -122.690, 45.560)
) AS s (s_idx, substation_code, short_name, rated_capacity_kva, lon, lat);

-- Feeders fan out from their substation; each runs about 2 km in a straight line.
CREATE TEMP TABLE seed_feeders ON COMMIT DROP AS
SELECT s.s_idx, f AS f_idx, s.substation_code, format('PDX-%s-F%s', s.short_name, f) AS feeder_code,
       s.lon AS lon1, s.lat AS lat1,
       s.lon + 0.025 * cos(radians(f * 120 - 30)) AS lon2,
       s.lat + 0.018 * sin(radians(f * 120 - 30)) AS lat2
FROM seed_substations s, generate_series(1, 3) AS f;

CREATE TEMP TABLE seed_transformers ON COMMIT DROP AS
SELECT f.s_idx, f.f_idx, k AS k_idx, f.feeder_code, format('%s-X%s', f.feeder_code, lpad(k::text, 2, '0')) AS transformer_code,
       CASE WHEN f.feeder_code = 'PDX-ARB-F1' AND k = 1 THEN 50.0 ELSE 150.0 END AS rated_capacity_kva,
       f.lon1 + (f.lon2 - f.lon1) * k / 10.0 AS lon,
       f.lat1 + (f.lat2 - f.lat1) * k / 10.0 AS lat
FROM seed_feeders f, generate_series(1, 10) AS k;

-- n numbers the homes 1..480 across the whole grid; it drives every per-home choice below.
CREATE TEMP TABLE seed_homes ON COMMIT DROP AS
SELECT ((x.s_idx - 1) * 3 + (x.f_idx - 1)) * 80 + (x.k_idx - 1) * 8 + h AS n,
       x.feeder_code, x.transformer_code, format('%s-H%s', x.transformer_code, h) AS premise_code,
       x.lon + 0.0004 * (h - 4.5) AS lon,
       x.lat + 0.0003 * (CASE WHEN h % 2 = 0 THEN 1 ELSE -1 END) AS lat
FROM seed_transformers x, generate_series(1, 8) AS h;

-- ---------------------------------------------------------------- hierarchy
INSERT INTO topology.substations (substation_code, rated_capacity_kva, location)
SELECT s.substation_code, s.rated_capacity_kva, ST_SetSRID(ST_MakePoint(s.lon, s.lat), 4326)::geography
FROM seed_substations s
WHERE NOT EXISTS (SELECT 1 FROM topology.substations e WHERE e.substation_code = s.substation_code)
ORDER BY s.s_idx;

INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva, line_geometry)
SELECT sub.substation_id, f.feeder_code, 12.47, 8000,
       ST_SetSRID(ST_MakeLine(ST_MakePoint(f.lon1, f.lat1), ST_MakePoint(f.lon2, f.lat2)), 4326)
FROM seed_feeders f
JOIN topology.substations sub ON sub.substation_code = f.substation_code
WHERE NOT EXISTS (SELECT 1 FROM topology.feeders e WHERE e.feeder_code = f.feeder_code)
ORDER BY f.s_idx, f.f_idx;

INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v, location)
SELECT fdr.feeder_id, x.transformer_code, x.rated_capacity_kva, 240, ST_SetSRID(ST_MakePoint(x.lon, x.lat), 4326)::geography
FROM seed_transformers x
JOIN topology.feeders fdr ON fdr.feeder_code = x.feeder_code
WHERE NOT EXISTS (SELECT 1 FROM topology.neighborhood_transformers e WHERE e.neighborhood_transformer_code = x.transformer_code)
ORDER BY x.s_idx, x.f_idx, x.k_idx;

-- Homes: 200 A service at 240 V (48 kVA). Battery homes with a backup gateway can island.
INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, export_limit_kw, can_island, location)
SELECT x.neighborhood_transformer_id, h.premise_code,
       (SELECT customer_segment_id FROM topology.customer_segments WHERE internal_code = 'residential'),
       48,
       CASE WHEN h.n % 16 = 0 THEN 10 END,
       h.n % 4 IN (0, 1),
       ST_SetSRID(ST_MakePoint(h.lon, h.lat), 4326)::geography
FROM seed_homes h
JOIN topology.neighborhood_transformers x ON x.neighborhood_transformer_code = h.transformer_code
WHERE NOT EXISTS (SELECT 1 FROM topology.premises e WHERE e.premise_code = h.premise_code)
ORDER BY h.n;

-- C&I sites served at primary voltage (the customer owns its transformer), one per feeder.
INSERT INTO topology.premises (feeder_id, premise_code, customer_segment_id, service_capacity_kva, export_limit_kw, can_island, location)
SELECT fdr.feeder_id, f.feeder_code || '-CI',
       (SELECT customer_segment_id FROM topology.customer_segments WHERE internal_code = 'commercial_industrial'),
       1500,
       CASE WHEN f.feeder_code = 'PDX-ARB-F2' THEN 0 END,
       f.feeder_code = 'PDX-CED-F3',
       ST_SetSRID(ST_MakePoint(f.lon2, f.lat2), 4326)::geography
FROM seed_feeders f
JOIN topology.feeders fdr ON fdr.feeder_code = f.feeder_code
WHERE NOT EXISTS (SELECT 1 FROM topology.premises e WHERE e.premise_code = f.feeder_code || '-CI')
ORDER BY f.s_idx, f.f_idx;

INSERT INTO topology.microgrids (microgrid_code, feeder_id, can_island, import_limit_kw, export_limit_kw)
SELECT 'PDX-MG-CED-F1', fdr.feeder_id, TRUE, 2000, 1500
FROM topology.feeders fdr
WHERE fdr.feeder_code = 'PDX-CED-F1'
  AND NOT EXISTS (SELECT 1 FROM topology.microgrids e WHERE e.microgrid_code = 'PDX-MG-CED-F1');

-- ---------------------------------------------------------------- assets and their components
-- One row per asset with exactly one connection (premise, transformer, feeder, or substation code), in its final
-- lifecycle and participation state. The lifecycle trigger's rules hold at insert: commissioned and decommissioned
-- assets have an in-service date, planned and installed ones do not, and only commissioned assets are active or
-- suspended.
CREATE TEMP TABLE seed_assets
(
    asset_code          varchar(50) PRIMARY KEY,
    premise_code        varchar(50),
    transformer_code    varchar(50),
    feeder_code         varchar(50),
    substation_code     varchar(50),
    microgrid_code      varchar(50),
    interconnection     varchar(50) NOT NULL,
    control_profile     varchar(50),
    lifecycle           varchar(50) NOT NULL,
    participation       varchar(50) NOT NULL,
    in_service_date     date,
    decommissioned_date date,
    rated_p_min_kw      double precision NOT NULL,
    rated_p_max_kw      double precision NOT NULL,
    rated_kva           double precision NOT NULL,
    design              varchar(50) NOT NULL    -- selects the components below
) ON COMMIT DROP;

-- Each home's main asset. Lifecycle and participation follow n, so the mix is the same on every run.
INSERT INTO seed_assets
SELECT h.premise_code || '-A1', h.premise_code, NULL, NULL, NULL,
       CASE WHEN h.transformer_code IN ('PDX-CED-F1-X01', 'PDX-CED-F1-X02', 'PDX-CED-F1-X03') THEN 'PDX-MG-CED-F1' END,
       'btm',
       (ARRAY['tesla_powerwall_3', 'enphase_iq_gateway', 'solaredge_se7600h', 'enphase_iq_gateway'])[h.n % 4 + 1],
       st.lifecycle, st.participation,
       CASE WHEN st.lifecycle IN ('commissioned', 'decommissioned') THEN DATE '2022-01-03' + h.n * 3 END,
       CASE WHEN st.lifecycle = 'decommissioned' THEN DATE '2022-01-03' + h.n * 3 + 300 END,
       (ARRAY[-11.5, -7.68, 0, -3.84])[h.n % 4 + 1],
       (ARRAY[11.5, 10, 7.6, 3.84])[h.n % 4 + 1],
       (ARRAY[11.5, 10, 7.6, 3.84])[h.n % 4 + 1],
       (ARRAY['tesla_pv', 'enphase_pv_battery', 'solaredge_pv', 'enphase_battery'])[h.n % 4 + 1]
FROM seed_homes h
CROSS JOIN LATERAL (SELECT
    CASE WHEN h.n % 43 = 0 THEN 'decommissioned'
         WHEN h.n % 41 = 0 THEN 'installed'
         WHEN h.n % 37 = 0 THEN 'planned'
         ELSE 'commissioned' END AS lifecycle,
    CASE WHEN h.n % 43 = 0 THEN 'opted_out'
         WHEN h.n % 41 = 0 OR h.n % 37 = 0 THEN 'not_enrolled'
         WHEN h.n % 29 = 0 THEN 'suspended'
         WHEN h.n % 31 = 0 THEN 'opted_out'
         WHEN h.n % 23 = 0 THEN 'not_enrolled'
         ELSE 'active' END AS participation) st;

-- An EV charger on every tenth home: a second asset at the premise, with its own controller (charging only).
INSERT INTO seed_assets
SELECT h.premise_code || '-A2', h.premise_code, NULL, NULL, NULL,
       CASE WHEN h.transformer_code IN ('PDX-CED-F1-X01', 'PDX-CED-F1-X02', 'PDX-CED-F1-X03') THEN 'PDX-MG-CED-F1' END,
       'btm', 'chargepoint_home_flex', 'commissioned', 'active', DATE '2024-06-01', NULL::date,
       -11.5, 0, 11.5, 'evse'
FROM seed_homes h
WHERE h.n % 10 = 0;

-- C&I sites: separate PV and battery systems, no control profile recorded (D21).
INSERT INTO seed_assets
SELECT f.feeder_code || '-CI-PV', f.feeder_code || '-CI', NULL, NULL, NULL, NULL, 'btm', NULL,
       'commissioned', 'active', DATE '2024-06-01', NULL::date, 0, 250, 250, 'ci_pv'
FROM seed_feeders f
UNION ALL
SELECT f.feeder_code || '-CI-BESS', f.feeder_code || '-CI', NULL, NULL, NULL, NULL, 'btm', NULL,
       'commissioned', 'active', DATE '2024-06-01', NULL::date, -125, 125, 125, 'ci_bess'
FROM seed_feeders f;

-- Utility-owned storage in front of any meter, at the three levels above the premise.
INSERT INTO seed_assets
SELECT f.feeder_code || '-X05-PB', NULL, f.feeder_code || '-X05', NULL, NULL, NULL, 'ftm', 'generic_pcs_50',
       'commissioned', 'active', DATE '2024-06-01', NULL::date, -50, 50, 50, 'pole_battery'
FROM seed_feeders f
UNION ALL
SELECT f.feeder_code || '-CB', NULL, NULL, f.feeder_code, NULL,
       CASE WHEN f.feeder_code = 'PDX-CED-F1' THEN 'PDX-MG-CED-F1' END,
       'ftm', 'generic_pcs_1000', 'commissioned', 'active', DATE '2024-06-01', NULL::date, -1000, 1000, 1000, 'community_battery'
FROM seed_feeders f
UNION ALL
SELECT s.substation_code || '-BESS', NULL, NULL, NULL, s.substation_code, NULL, 'ftm', 'generic_pcs_2500',
       'commissioned', 'active', DATE '2024-06-01', NULL::date, -2500, 2500, 2500, 'substation_battery'
FROM seed_substations s;

-- What each design is made of. rated_kw / rated_kwh are installed totals (unit_count x per-unit); an asset's rating
-- is its connection-point limit, not the sum of these.
CREATE TEMP TABLE seed_design_components ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('tesla_pv',           'bess',     'tesla_powerwall_3',         1,   11.5,  13.5),
    ('tesla_pv',           'solar_pv', 'generic_pv_module_400w',    20,  8.0,   NULL),
    ('enphase_pv_battery', 'bess',     'enphase_iq_battery_5p',     2,   7.68,  10.0),
    ('enphase_pv_battery', 'solar_pv', 'generic_pv_module_400w',    24,  9.6,   NULL),
    ('solaredge_pv',       'solar_pv', 'generic_pv_module_400w',    22,  8.8,   NULL),
    ('enphase_battery',    'bess',     'enphase_iq_battery_5p',     1,   3.84,  5.0),
    ('evse',               'evse',     'chargepoint_home_flex',     1,   11.5,  NULL),
    ('ci_pv',              'solar_pv', 'generic_pv_module_550w',    500, 275.0, NULL),
    ('ci_bess',            'bess',     'generic_ci_bess_cabinet',   1,   125.0, 250.0),
    ('pole_battery',       'bess',     'generic_pole_battery',      1,   50.0,  100.0),
    ('community_battery',  'bess',     'generic_utility_bess_2mwh', 1,   1000.0, 2000.0),
    ('substation_battery', 'bess',     'generic_utility_bess_2mwh', 3,   3000.0, 6000.0)
) AS c (design, technology_code, hardware_profile_code, unit_count, rated_kw, rated_kwh);

-- Assets and their components in one statement: components are added only for assets this run inserted, so a
-- re-run (where every asset already exists) adds none.
WITH inserted AS (
    INSERT INTO topology.assets
        (asset_code, premise_id, neighborhood_transformer_id, feeder_id, substation_id, microgrid_id, interconnection_type_id,
         control_hardware_profile_id, asset_lifecycle_state_id, participation_state_id, in_service_date, decommissioned_date,
         rated_p_min_kw, rated_p_max_kw, rated_kva)
    SELECT a.asset_code, p.premise_id, x.neighborhood_transformer_id, f.feeder_id, s.substation_id, m.microgrid_id,
           it.interconnection_type_id, hp.hardware_profile_id,
           topology.asset_lifecycle_state_id(a.lifecycle), topology.participation_state_id(a.participation),
           a.in_service_date, a.decommissioned_date, a.rated_p_min_kw, a.rated_p_max_kw, a.rated_kva
    FROM seed_assets a
    JOIN topology.interconnection_types it ON it.internal_code = a.interconnection
    LEFT JOIN topology.premises p ON p.premise_code = a.premise_code
    LEFT JOIN topology.neighborhood_transformers x ON x.neighborhood_transformer_code = a.transformer_code
    LEFT JOIN topology.feeders f ON f.feeder_code = a.feeder_code
    LEFT JOIN topology.substations s ON s.substation_code = a.substation_code
    LEFT JOIN topology.microgrids m ON m.microgrid_code = a.microgrid_code
    LEFT JOIN topology.hardware_profiles hp ON hp.hardware_profile_code = a.control_profile
    ORDER BY a.asset_code
    ON CONFLICT (asset_code) DO NOTHING
    RETURNING asset_id, asset_code
)
INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw, rated_kwh)
SELECT i.asset_id, t.technology_type_id, hp.hardware_profile_id, c.unit_count, c.rated_kw, c.rated_kwh
FROM inserted i
JOIN seed_assets a USING (asset_code)
JOIN seed_design_components c ON c.design = a.design
JOIN topology.technology_types t ON t.internal_code = c.technology_code
JOIN topology.hardware_profiles hp ON hp.hardware_profile_code = c.hardware_profile_code
ORDER BY i.asset_code, c.technology_code;

-- A seed asset that is missing now was rejected by a join above (an unknown code in this file): fail rather than
-- commit a partial topology.
DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(a.asset_code, ', ' ORDER BY a.asset_code) INTO missing
    FROM seed_assets a
    WHERE NOT EXISTS (SELECT 1 FROM topology.assets e WHERE e.asset_code = a.asset_code);
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'dev_topology.sql: assets not seeded (unknown code in the seed): %', missing;
    END IF;
END $$;
