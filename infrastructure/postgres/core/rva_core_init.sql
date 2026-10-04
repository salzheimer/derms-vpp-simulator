/*
Description: database initialization script for rva_core (PostgreSQL 18 + PostGIS).
Each backend module owns one schema; there are no foreign keys across schemas.
  topology   - GridTopology module: lookups, hardware profiles, grid hierarchy, microgrids, assets
  simulation - Simulation module: run ledger and per-run asset overrides
Cross-module references (e.g. simulation_asset_states.asset_id) are plain IDs validated by the owning
module's contracts. See .ai/rules/csharp-monolith.md and .ai/plans/PHASE_1_PLAN.md.

Asset model:
  asset            = a grid-facing controllable resource: everything behind one control interface,
                     at one connection point, with one rating at that point (what dispatch sees)
  asset_components = what the asset is made of (PV array, battery modules); used for simulation, never dispatched
  premise          = a customer site behind one utility meter (the point of common coupling); a behind-the-meter
                     microgrid is a premise with can_island = TRUE
  microgrid        = a utility-side grouping spanning several premises and/or feeder-connected assets
Limits: site (PCC) limits live on premises and microgrids; per-system (point of DER connection) limits on assets.
Dispatch must respect both.
*/

-- Enable standard spatial queries. Installed in public so PostGIS types and functions resolve without a schema prefix.
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE SCHEMA IF NOT EXISTS topology;
CREATE SCHEMA IF NOT EXISTS simulation;


-- =====================================================================
-- topology: lookups
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.technology_types
(
    technology_type_id  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL
);
COMMENT ON TABLE topology.technology_types IS 'what kind of energy source or storage a component is, ex. BESS, Solar PV. Technology only - customer segment and size are modeled separately';
COMMENT ON COLUMN topology.technology_types.internal_code IS 'unique internal code for identifying the tech type';

INSERT INTO topology.technology_types (internal_code, display_name) VALUES
('bess',     'Battery Energy Storage System'),
('solar_pv', 'Solar PV'),
('wind',     'Wind'),
('evse',     'EV Charger (EVSE)')
ON CONFLICT (internal_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS topology.protocol_mappings
(
    protocol_mapping_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    technology_type_id      INTEGER NOT NULL REFERENCES topology.technology_types (technology_type_id) ON DELETE CASCADE,
    protocol_standard       VARCHAR(50) NOT NULL,   -- e.g., 'OpenADR_3.0', 'IEEE_2030_5'
    resource_category       VARCHAR(100) NOT NULL,  -- e.g., 'Storage'
    asset_type_code         VARCHAR(100) NOT NULL,  -- e.g., 'Battery'
    extra_attributes        JSONB NULL,             -- For edge-case capabilities
    CONSTRAINT uk_protocol_mapping UNIQUE (protocol_standard, resource_category, asset_type_code)
);
COMMENT ON TABLE topology.protocol_mappings IS 'Protocol Mappings is used for mapping internal technology types to external protocols such as OpenADR and IEEE 2030.5';
COMMENT ON COLUMN topology.protocol_mappings.technology_type_id IS 'reference to technology types';
COMMENT ON COLUMN topology.protocol_mappings.protocol_standard IS 'tracks the protocol standard and version ex. OpenADR_2.0b, OpenADR_3.0, IEEE_2030_5';
COMMENT ON COLUMN topology.protocol_mappings.resource_category IS 'the external standard''s resource category for this technology, ex. Storage';
COMMENT ON COLUMN topology.protocol_mappings.asset_type_code IS 'the external standard''s asset/device type code, ex. Battery';
COMMENT ON COLUMN topology.protocol_mappings.extra_attributes IS 'additional external attributes or capabilities needed to identify the technology, as JSON';
CREATE INDEX IF NOT EXISTS ix_protocol_mappings_technology_type_id ON topology.protocol_mappings (technology_type_id);

CREATE TABLE IF NOT EXISTS topology.interconnection_types
(
    interconnection_type_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code               VARCHAR(50) NOT NULL UNIQUE,  -- machine code
    display_name                VARCHAR(100) NOT NULL
);
COMMENT ON TABLE topology.interconnection_types IS 'meter position of an asset relative to the customer meter. Only ambiguous for premise-connected assets; feeder/substation-connected assets are always ftm (enforced in the Asset entity)';

INSERT INTO topology.interconnection_types (internal_code, display_name) VALUES
('btm', 'Behind-the-Meter'),
('ftm', 'In-Front-of-the-Meter'),
('atm', 'Across-the-Meter')
ON CONFLICT (internal_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS topology.customer_segments
(
    customer_segment_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code           VARCHAR(50) NOT NULL UNIQUE,
    display_name            VARCHAR(100) NOT NULL
);
COMMENT ON TABLE topology.customer_segments IS 'the utility service class of a premise, ex. Residential. Independent of equipment size';

INSERT INTO topology.customer_segments (internal_code, display_name) VALUES
('residential',           'Residential'),
('commercial_industrial', 'Commercial & Industrial')
ON CONFLICT (internal_code) DO NOTHING;


-- =====================================================================
-- topology: hardware profiles (equipment models)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.hardware_profiles
(
    hardware_profile_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    hardware_profile_code   VARCHAR(50) NOT NULL UNIQUE,    -- e.g. 'tesla_powerwall_3'
    manufacturer            VARCHAR(100) NOT NULL,           -- selects the device-integration adapter
    model                   VARCHAR(100) NOT NULL,
    technology_type_id      INTEGER NULL REFERENCES topology.technology_types (technology_type_id),  -- NULL = conversion/control equipment (inverter, gateway, plant controller)
    rated_kw                DOUBLE PRECISION NULL CHECK (rated_kw > 0),    -- per unit: battery continuous power, PV module kW-dc, or inverter kW-ac
    rated_kva               DOUBLE PRECISION NULL CHECK (rated_kva > 0),   -- per unit, inverter apparent power
    rated_kwh               DOUBLE PRECISION NULL CHECK (rated_kwh > 0),   -- per unit, storage only
    round_trip_efficiency   DOUBLE PRECISION NULL CHECK (round_trip_efficiency > 0 AND round_trip_efficiency <= 1),
    CONSTRAINT uk_hardware_profile_technology UNIQUE (hardware_profile_id, technology_type_id)  -- target for the component technology match
);
COMMENT ON TABLE topology.hardware_profiles IS 'manufacturer equipment models with per-unit nameplate specs, used by asset components (physics) and assets (control/integration routing)';


-- =====================================================================
-- topology: hierarchy (substation -> feeder -> neighborhood transformer -> premise)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.substations
(
    substation_id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    substation_code         VARCHAR(50) NOT NULL UNIQUE,
    rated_capacity_kva      DOUBLE PRECISION NOT NULL CHECK (rated_capacity_kva > 0),
    location                GEOGRAPHY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS topology.feeders
(
    feeder_id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    substation_id           BIGINT NOT NULL REFERENCES topology.substations (substation_id),
    feeder_code             VARCHAR(50) NOT NULL UNIQUE,
    nominal_voltage_kv      DOUBLE PRECISION NOT NULL CHECK (nominal_voltage_kv > 0),   -- e.g. 12.47
    rated_capacity_kva      DOUBLE PRECISION NULL CHECK (rated_capacity_kva > 0),
    line_geometry           GEOMETRY(LineString, 4326)
);
CREATE INDEX IF NOT EXISTS ix_feeders_substation_id ON topology.feeders (substation_id);

CREATE TABLE IF NOT EXISTS topology.neighborhood_transformers
(
    neighborhood_transformer_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    feeder_id                       BIGINT NOT NULL REFERENCES topology.feeders (feeder_id),
    neighborhood_transformer_code   VARCHAR(50) NOT NULL UNIQUE,
    rated_capacity_kva              DOUBLE PRECISION NOT NULL CHECK (rated_capacity_kva > 0),   -- e.g. 50
    secondary_voltage_v             DOUBLE PRECISION NOT NULL CHECK (secondary_voltage_v > 0),  -- e.g. 240
    location                        GEOGRAPHY(Point, 4326)
);
CREATE INDEX IF NOT EXISTS ix_neighborhood_transformers_feeder_id ON topology.neighborhood_transformers (feeder_id);

CREATE TABLE IF NOT EXISTS topology.premises
(
    premise_id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    neighborhood_transformer_id     BIGINT NOT NULL REFERENCES topology.neighborhood_transformers (neighborhood_transformer_id),
    premise_code                    VARCHAR(50) NOT NULL UNIQUE,
    customer_segment_id             INTEGER NOT NULL REFERENCES topology.customer_segments (customer_segment_id),
    service_capacity_kva            DOUBLE PRECISION NULL CHECK (service_capacity_kva > 0),  -- import limit: service size, ex. 200 A at 240 V = 48 kVA
    export_limit_kw                 DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),      -- site export limit from the interconnection agreement; 0 = non-export
    can_island                      BOOLEAN NOT NULL DEFAULT FALSE,                          -- backup gateway / site microgrid controller present
    address                         VARCHAR(255),
    location                        GEOGRAPHY(Point, 4326)
);
COMMENT ON TABLE topology.premises IS 'a customer site behind one utility meter (point of common coupling). All assets at a premise island together when it islands';
CREATE INDEX IF NOT EXISTS ix_premises_neighborhood_transformer_id ON topology.premises (neighborhood_transformer_id);


-- =====================================================================
-- topology: microgrids and assets
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.microgrids
(
    microgrid_id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    microgrid_code          VARCHAR(50) NOT NULL UNIQUE,
    feeder_id               BIGINT NULL REFERENCES topology.feeders (feeder_id),
    substation_id           BIGINT NULL REFERENCES topology.substations (substation_id),
    can_island              BOOLEAN NOT NULL DEFAULT TRUE,
    import_limit_kw         DOUBLE PRECISION NULL CHECK (import_limit_kw >= 0),  -- at the microgrid's point of common coupling
    export_limit_kw         DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),
    CONSTRAINT ck_microgrids_single_connection CHECK (num_nonnulls(feeder_id, substation_id) = 1)
);
COMMENT ON TABLE topology.microgrids IS 'utility-side (FTM) microgrid spanning several premises and/or feeder-connected assets. A behind-the-meter microgrid is represented by its premise (can_island and site limits)';
CREATE INDEX IF NOT EXISTS ix_microgrids_feeder_id ON topology.microgrids (feeder_id);
CREATE INDEX IF NOT EXISTS ix_microgrids_substation_id ON topology.microgrids (substation_id);

CREATE TABLE IF NOT EXISTS topology.assets
(
    asset_id                        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_code                      VARCHAR(50) NOT NULL UNIQUE,
    -- connection point: exactly one level of the hierarchy
    premise_id                      BIGINT NULL REFERENCES topology.premises (premise_id),
    feeder_id                       BIGINT NULL REFERENCES topology.feeders (feeder_id),
    substation_id                   BIGINT NULL REFERENCES topology.substations (substation_id),
    microgrid_id                    BIGINT NULL REFERENCES topology.microgrids (microgrid_id),
    interconnection_type_id         INTEGER NOT NULL REFERENCES topology.interconnection_types (interconnection_type_id),
    control_hardware_profile_id     INTEGER NULL REFERENCES topology.hardware_profiles (hardware_profile_id),  -- the controller commands go through; its manufacturer selects the integration adapter
    -- ratings at the connection point (what dispatch uses); not the sum of components. Negative = charging / import
    rated_p_min_kw                  DOUBLE PRECISION NOT NULL,
    rated_p_max_kw                  DOUBLE PRECISION NOT NULL,
    rated_kva                       DOUBLE PRECISION NOT NULL CHECK (rated_kva > 0),
    export_limit_kw                 DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),  -- per-system limit at the point of DER connection; site limits are on premises/microgrids
    CONSTRAINT ck_assets_single_connection CHECK (num_nonnulls(premise_id, feeder_id, substation_id) = 1),
    CONSTRAINT ck_assets_power_bounds      CHECK (rated_p_min_kw <= rated_p_max_kw),
    CONSTRAINT ck_assets_power_within_kva  CHECK (GREATEST(ABS(rated_p_min_kw), ABS(rated_p_max_kw)) <= rated_kva)
);
COMMENT ON TABLE topology.assets IS 'a grid-facing controllable resource: everything behind one control interface at one connection point. Makeup (PV only, storage, PV + storage) and energy capacity are derived from asset_components';
CREATE INDEX IF NOT EXISTS ix_assets_premise_id    ON topology.assets (premise_id);
CREATE INDEX IF NOT EXISTS ix_assets_feeder_id     ON topology.assets (feeder_id);
CREATE INDEX IF NOT EXISTS ix_assets_substation_id ON topology.assets (substation_id);
CREATE INDEX IF NOT EXISTS ix_assets_microgrid_id  ON topology.assets (microgrid_id);

CREATE TABLE IF NOT EXISTS topology.asset_components
(
    asset_component_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id                BIGINT NOT NULL REFERENCES topology.assets (asset_id) ON DELETE CASCADE,
    technology_type_id      INTEGER NOT NULL REFERENCES topology.technology_types (technology_type_id),
    hardware_profile_id     INTEGER NULL,
    unit_count              INTEGER NOT NULL DEFAULT 1 CHECK (unit_count > 0),
    rated_kw                DOUBLE PRECISION NULL CHECK (rated_kw >= 0),    -- installed total: PV kW-dc, battery power
    rated_kwh               DOUBLE PRECISION NULL CHECK (rated_kwh >= 0),   -- installed total, storage only
    -- a component's profile must be an energy product of the same technology (rejects control-only equipment)
    CONSTRAINT fk_asset_components_hardware_profile FOREIGN KEY (hardware_profile_id, technology_type_id)
        REFERENCES topology.hardware_profiles (hardware_profile_id, technology_type_id)
);
COMMENT ON TABLE topology.asset_components IS 'what an asset is made of, for simulation. Components are never dispatched directly';
CREATE INDEX IF NOT EXISTS ix_asset_components_asset_id ON topology.asset_components (asset_id);


-- =====================================================================
-- simulation: run ledger and per-run asset overrides
-- =====================================================================

CREATE TABLE IF NOT EXISTS simulation.simulation_run_status_types
(
    status_type_id      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                VARCHAR(50) NOT NULL UNIQUE,
    description         VARCHAR(255)
);

INSERT INTO simulation.simulation_run_status_types (name, description) VALUES
('Pending',   'Initialized but waiting for resources or worker allocation.'),
('Running',   'Actively ticking and streaming telemetry.'),
('Completed', 'Finished successfully within its allocated time window.'),
('Failed',    'Aborted due to an unhandled exception, configuration error, or worker crash.')
ON CONFLICT (name) DO NOTHING;

CREATE TABLE IF NOT EXISTS simulation.simulation_runs
(
    simulation_run_id   UUID PRIMARY KEY DEFAULT uuidv7(),  -- time-ordered UUID (PostgreSQL 18+)
    scenario_name       VARCHAR(100) NOT NULL,
    start_time          TIMESTAMPTZ NOT NULL,
    end_time            TIMESTAMPTZ NULL,
    status_type_id      INTEGER NOT NULL REFERENCES simulation.simulation_run_status_types (status_type_id),
    configuration_json  JSONB,  -- scenario rules; refers to assets by asset_code
    CONSTRAINT ck_simulation_runs_time_window CHECK (end_time IS NULL OR end_time >= start_time)
);

CREATE TABLE IF NOT EXISTS simulation.simulation_asset_states
(
    simulation_asset_state_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    simulation_run_id           UUID NOT NULL REFERENCES simulation.simulation_runs (simulation_run_id),
    asset_id                    BIGINT NOT NULL,  -- owned by GridTopology; validated via IGridTopologyQueries, no FK by design
    is_available                BOOLEAN NOT NULL DEFAULT TRUE,
    p_min_kw                    DOUBLE PRECISION NULL,  -- NULL = no override; use the asset's own bound
    p_max_kw                    DOUBLE PRECISION NULL,
    state_of_charge_pct         DOUBLE PRECISION NULL,
    CONSTRAINT uk_simulation_asset UNIQUE (simulation_run_id, asset_id),
    CONSTRAINT ck_simulation_asset_states_power_bounds CHECK (p_min_kw IS NULL OR p_max_kw IS NULL OR p_min_kw <= p_max_kw),
    CONSTRAINT ck_simulation_asset_states_soc CHECK (state_of_charge_pct IS NULL OR state_of_charge_pct BETWEEN 0 AND 100)
);
