/*
Catalog seed for rva_core (plan p1-s01b, D12): hardware profiles and protocol mappings. Run by seed.sh, inside the one
transaction it opens, before dev_topology.sql. Idempotent: rows are keyed on their natural codes, and a code that
already exists is left as it is (ON CONFLICT DO NOTHING). Inserts write no history rows, so a re-run changes nothing.

A code whose stored row differs from the values below is never overwritten: the drift check at the end reports it as a
WARNING, so a changed seed value is visible without a silent update. Changing a catalog row is a deliberate migration.

Values are NOMINAL DEVELOPMENT DATA: rounded per-unit figures in the shape of public datasheets, and placeholder
protocol codes, not engineering or certified data. Verify each against the manufacturer datasheet and the protocol
standard before any value is relied on outside development.
*/

DO $$
BEGIN
    PERFORM set_config('rva.actor', '019a3c00-0000-7000-8000-0000000000a5', true);   -- the development seed, a system actor
    PERFORM set_config('rva.actor_type', 'system', true);
    PERFORM set_config('rva.change_reason', 'development seed: catalog', true);
END $$;

-- ---------------------------------------------------------------- hardware profiles
-- technology_code NULL = conversion or control equipment (inverter, gateway, PCS): an asset's control profile only,
-- never a component (the composite FK on asset_components rejects it).
CREATE TEMP TABLE seed_hardware_profiles
(
    hardware_profile_code   varchar(50) PRIMARY KEY,
    manufacturer            varchar(100) NOT NULL,
    model                   varchar(100) NOT NULL,
    technology_code         varchar(50),
    rated_kw                double precision,
    rated_kva               double precision,
    rated_kwh               double precision,
    round_trip_efficiency   double precision
) ON COMMIT DROP;

INSERT INTO seed_hardware_profiles VALUES
-- residential storage and hybrid inverters
('tesla_powerwall_3',         'Tesla',       'Powerwall 3',                     'bess',     11.5,   11.5,   13.5,   0.90),
('enphase_iq_battery_5p',     'Enphase',     'IQ Battery 5P',                   'bess',     3.84,   3.84,   5.0,    0.90),
('enphase_iq_gateway',        'Enphase',     'IQ Gateway',                      NULL,       NULL,   NULL,   NULL,   NULL),
('enphase_iq8m',              'Enphase',     'IQ8M Microinverter',              NULL,       0.325,  0.33,   NULL,   NULL),
('solaredge_se7600h',         'SolarEdge',   'SE7600H Home Wave Inverter',      NULL,       7.6,    7.6,    NULL,   NULL),
-- PV modules (rated_kw is kW-dc per module)
('generic_pv_module_400w',    'Generic',     'Residential PV Module 400 W',     'solar_pv', 0.4,    NULL,   NULL,   NULL),
('generic_pv_module_550w',    'Generic',     'Commercial PV Module 550 W',      'solar_pv', 0.55,   NULL,   NULL,   NULL),
-- EV charging
('chargepoint_home_flex',     'ChargePoint', 'Home Flex (48 A)',                'evse',     11.5,   11.5,   NULL,   NULL),
-- commercial and utility storage, and the power conversion systems that control it
('generic_ci_bess_cabinet',   'Generic',     'C&I Battery Cabinet 125 kW / 250 kWh', 'bess', 125,   NULL,   250,    0.89),
('generic_pole_battery',      'Generic',     'Pole-mount Battery 50 kW / 100 kWh',   'bess', 50,    NULL,   100,    0.88),
('generic_utility_bess_2mwh', 'Generic',     'Utility BESS Container 1 MW / 2 MWh',  'bess', 1000,  NULL,   2000,   0.88),
('generic_pcs_50',            'Generic',     'Power Conversion System 50 kVA',  NULL,       50,     50,     NULL,   NULL),
('generic_pcs_1000',          'Generic',     'Power Conversion System 1 MVA',   NULL,       1000,   1000,   NULL,   NULL),
('generic_pcs_2500',          'Generic',     'Power Conversion System 2.5 MVA', NULL,       2500,   2500,   NULL,   NULL);

INSERT INTO topology.hardware_profiles
    (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw, rated_kva, rated_kwh, round_trip_efficiency)
SELECT s.hardware_profile_code, s.manufacturer, s.model, t.technology_type_id, s.rated_kw, s.rated_kva, s.rated_kwh,
       s.round_trip_efficiency
FROM seed_hardware_profiles s
LEFT JOIN topology.technology_types t ON t.internal_code = s.technology_code
ORDER BY s.hardware_profile_code
ON CONFLICT (hardware_profile_code) DO NOTHING;

-- ---------------------------------------------------------------- protocol mappings
-- One row per (standard, resource category, asset type code). OpenADR 3.0 rows use the resource and asset type names
-- the Phase 1 plan uses; IEEE 2030.5 rows map the DERType code a device reports in its DERCapability.
CREATE TEMP TABLE seed_protocol_mappings
(
    protocol_standard   varchar(50),
    resource_category   varchar(100),
    asset_type_code     varchar(100),
    technology_code     varchar(50) NOT NULL,
    extra_attributes    jsonb,
    PRIMARY KEY (protocol_standard, resource_category, asset_type_code)
) ON COMMIT DROP;

INSERT INTO seed_protocol_mappings VALUES
('OpenADR_3.0', 'Storage',    'Battery', 'bess',     NULL),
('OpenADR_3.0', 'Generation', 'SolarPV', 'solar_pv', NULL),
('OpenADR_3.0', 'Generation', 'Wind',    'wind',     NULL),
('OpenADR_3.0', 'Load',       'EVSE',    'evse',     NULL),
('IEEE_2030_5', 'DER',        '4',       'solar_pv', '{"derType": 4, "derTypeName": "Photovoltaic system"}'),
('IEEE_2030_5', 'DER',        '80',      'bess',     '{"derType": 80, "derTypeName": "Other storage system"}'),
('IEEE_2030_5', 'DER',        '82',      'evse',     '{"derType": 82, "derTypeName": "Electric vehicle supply equipment"}');

INSERT INTO topology.protocol_mappings (technology_type_id, protocol_standard, resource_category, asset_type_code, extra_attributes)
SELECT t.technology_type_id, s.protocol_standard, s.resource_category, s.asset_type_code, s.extra_attributes
FROM seed_protocol_mappings s
JOIN topology.technology_types t ON t.internal_code = s.technology_code
ORDER BY s.protocol_standard, s.resource_category, s.asset_type_code
ON CONFLICT (protocol_standard, resource_category, asset_type_code) DO NOTHING;

-- ---------------------------------------------------------------- drift check
-- Every seed row must now exist as written. A row that already existed with other values was kept (DO NOTHING); report
-- it so the difference is seen, not silently ignored.
DO $$
DECLARE
    drifted text;
BEGIN
    SELECT string_agg(format('topology.hardware_profiles.%s', s.hardware_profile_code), ', ' ORDER BY s.hardware_profile_code)
    INTO drifted
    FROM seed_hardware_profiles s
    JOIN topology.hardware_profiles h USING (hardware_profile_code)
    LEFT JOIN topology.technology_types t ON t.technology_type_id = h.technology_type_id
    WHERE (h.manufacturer, h.model, t.internal_code, h.rated_kw, h.rated_kva, h.rated_kwh, h.round_trip_efficiency)
          IS DISTINCT FROM
          (s.manufacturer, s.model, s.technology_code, s.rated_kw, s.rated_kva, s.rated_kwh, s.round_trip_efficiency);
    IF drifted IS NOT NULL THEN
        RAISE WARNING 'catalog drift: % differ from catalog.sql and were left unchanged', drifted;
    END IF;

    SELECT string_agg(format('topology.protocol_mappings(%s, %s, %s)', s.protocol_standard, s.resource_category, s.asset_type_code),
                      ', ' ORDER BY s.protocol_standard, s.resource_category, s.asset_type_code)
    INTO drifted
    FROM seed_protocol_mappings s
    JOIN topology.protocol_mappings m USING (protocol_standard, resource_category, asset_type_code)
    JOIN topology.technology_types t ON t.technology_type_id = m.technology_type_id
    WHERE (t.internal_code, m.extra_attributes) IS DISTINCT FROM (s.technology_code, s.extra_attributes);
    IF drifted IS NOT NULL THEN
        RAISE WARNING 'catalog drift: % differ from catalog.sql and were left unchanged', drifted;
    END IF;
END $$;
