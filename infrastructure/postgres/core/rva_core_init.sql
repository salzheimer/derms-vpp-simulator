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
  grid node        = a substation, feeder, neighborhood transformer, premise, or microgrid. Each has its own table and id,
                     plus a grid_node_id from topology.grid_nodes: one id space across levels for node-keyed data
                     (operating envelopes, node measurements, the future electrical network model)
Limits: site (PCC) limits live on premises and microgrids; per-system (point of DER connection) limits on assets.
Dispatch must respect both.
Asset status: each asset has a physical lifecycle (planned, installed, commissioned, decommissioned, with field dates)
and a program participation state (not_enrolled, active, suspended, opted_out), both versioned. Decommissioning is a
lifecycle change that keeps the row; DELETE only corrects records entered in error. Real-time availability (online,
maintenance) is operational state from telemetry, not stored here.
Grid state: connectivity and energization (connected_energized, connected_deenergized, islanded, disconnected) change on
operational timescales, so they are append-only events on grid nodes or assets (grid_state_events), read through
latest_grid_states. An asset's effective state is derived from its path by the GridTopology module.
Hierarchy foreign keys record the NORMAL (as-designed) configuration, not the currently energized path. A future
electrical network model (buses, line segments, switches) is added as new tables that reference these ones, never
the reverse.

Change tracking (csharp-monolith.md section 9):
  topology   - state that gets edited is SYSTEM-VERSIONED: every table has created_at and sys_period, a
               <table>_history twin, a versioning trigger, and a <table>_as_of(timestamptz) function, all set up by
               topology.enable_versioning(<table>). After any ALTER TABLE on a versioned table, call it again.
               lower(sys_period) is when the current version took effect (it doubles as updated_at).
               Who/why come from per-transaction settings set by the module:
                 SET LOCAL rva.actor = '<uuid>'; SET LOCAL rva.actor_type = 'user|service|system';
                 SET LOCAL rva.change_reason = '...';
  simulation - things that happen are APPEND-ONLY EVENTS: run lifecycle transitions are recorded in
               simulation_run_events by trigger, and run inputs cannot change once a run leaves Pending.
  both       - an outbox_messages table per module for reliable integration events.

Comments: every table and column has one, in the format of .ai/rules/sql-ddl.md (a purpose, then a [facts] tag that
infrastructure/postgres/tests/ddl_lint.sql checks against the catalog). topology.enable_versioning writes the comments
on created_at, sys_period, and the _history twins.
*/

-- Enable standard spatial queries. Installed in public so PostGIS types and functions resolve without a schema prefix.
CREATE EXTENSION IF NOT EXISTS postgis;
-- Lets a GiST index combine a scalar key with a time range (history lookups by id and period).
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE SCHEMA IF NOT EXISTS topology;
CREATE SCHEMA IF NOT EXISTS simulation;


-- =====================================================================
-- topology: lookups
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.technology_types
(
    technology_type_id  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period          TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.technology_types IS 'what kind of energy source or storage a component is, ex. BESS, Solar PV. Technology only - customer segment and size are modeled separately';
COMMENT ON COLUMN topology.technology_types.technology_type_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.technology_types.internal_code IS 'Stable machine code used by code and seed scripts, ex. bess, solar_pv. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.technology_types.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.protocol_mappings
(
    protocol_mapping_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    technology_type_id      INTEGER NOT NULL REFERENCES topology.technology_types (technology_type_id) ON DELETE CASCADE,
    protocol_standard       VARCHAR(50) NOT NULL,
    resource_category       VARCHAR(100) NOT NULL,
    asset_type_code         VARCHAR(100) NOT NULL,
    extra_attributes        JSONB NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    CONSTRAINT uk_protocol_mapping UNIQUE (protocol_standard, resource_category, asset_type_code)
);
COMMENT ON TABLE topology.protocol_mappings IS 'maps an internal technology type to the codes an external protocol (OpenADR, IEEE 2030.5) uses for it, so device messages can be translated at the Anti-Corruption Layer. One row per (standard, category, type code)';
COMMENT ON COLUMN topology.protocol_mappings.protocol_mapping_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.protocol_mappings.technology_type_id IS 'Internal technology type the external codes map to. Mappings are deleted with their technology type. [INTEGER; NOT NULL; FK topology.technology_types(technology_type_id) ON DELETE CASCADE]';
COMMENT ON COLUMN topology.protocol_mappings.protocol_standard IS 'External protocol standard and version, ex. OpenADR_2.0b, OpenADR_3.0, IEEE_2030_5. [VARCHAR(50); NOT NULL; UNIQUE (protocol_standard, resource_category, asset_type_code)]';
COMMENT ON COLUMN topology.protocol_mappings.resource_category IS 'The external standard''s resource category for this technology, ex. Storage. [VARCHAR(100); NOT NULL; UNIQUE (protocol_standard, resource_category, asset_type_code)]';
COMMENT ON COLUMN topology.protocol_mappings.asset_type_code IS 'The external standard''s asset or device type code, ex. Battery. [VARCHAR(100); NOT NULL; UNIQUE (protocol_standard, resource_category, asset_type_code)]';
COMMENT ON COLUMN topology.protocol_mappings.extra_attributes IS 'Additional external attributes or capabilities needed to identify the technology, as JSON. NULL = none. [JSONB; NULL]';
CREATE INDEX IF NOT EXISTS ix_protocol_mappings_technology_type_id ON topology.protocol_mappings (technology_type_id);

CREATE TABLE IF NOT EXISTS topology.interconnection_types
(
    interconnection_type_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code               VARCHAR(50) NOT NULL UNIQUE,
    display_name                VARCHAR(100) NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                  TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.interconnection_types IS 'meter position of an asset relative to the customer meter. Only ambiguous for premise-connected assets; feeder/substation-connected assets are always ftm (enforced in the Asset entity)';
COMMENT ON COLUMN topology.interconnection_types.interconnection_type_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.interconnection_types.internal_code IS 'Stable machine code: btm (behind the meter), ftm (in front of the meter), atm (across the meter). [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.interconnection_types.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.customer_segments
(
    customer_segment_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code           VARCHAR(50) NOT NULL UNIQUE,
    display_name            VARCHAR(100) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.customer_segments IS 'the utility service class of a premise, ex. Residential. Independent of equipment size';
COMMENT ON COLUMN topology.customer_segments.customer_segment_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.customer_segments.internal_code IS 'Stable machine code, ex. residential, commercial_industrial. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.customer_segments.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.grid_node_types
(
    grid_node_type_id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period          TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.grid_node_types IS 'which table a grid node lives in: substation, feeder, neighborhood_transformer, premise, microgrid';
COMMENT ON COLUMN topology.grid_node_types.grid_node_type_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.grid_node_types.internal_code IS 'The node table''s name in the singular, ex. substation, neighborhood_transformer. topology.assign_grid_node looks node types up by this code. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.grid_node_types.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.asset_lifecycle_states
(
    asset_lifecycle_state_id    INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code               VARCHAR(50) NOT NULL UNIQUE,
    display_name                VARCHAR(100) NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                  TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.asset_lifecycle_states IS 'physical lifecycle of an asset: planned, installed, commissioned (in service), decommissioned. Only commissioned assets are part of the operating grid';
COMMENT ON COLUMN topology.asset_lifecycle_states.asset_lifecycle_state_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.asset_lifecycle_states.internal_code IS 'Stable machine code: planned, installed, commissioned, decommissioned. topology.check_asset_lifecycle uses these codes. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.asset_lifecycle_states.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.participation_states
(
    participation_state_id      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code               VARCHAR(50) NOT NULL UNIQUE,
    display_name                VARCHAR(100) NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                  TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.participation_states IS 'whether the VPP may use an asset: not_enrolled, active, suspended, opted_out. Independent of the physical lifecycle, except that only a commissioned asset can be active or suspended';
COMMENT ON COLUMN topology.participation_states.participation_state_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.participation_states.internal_code IS 'Stable machine code: not_enrolled, active, suspended, opted_out. topology.check_asset_lifecycle uses these codes. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.participation_states.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

CREATE TABLE IF NOT EXISTS topology.grid_states
(
    grid_state_id       INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL,
    is_grid_connected   BOOLEAN NOT NULL,
    is_energized        BOOLEAN NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period          TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.grid_states IS 'connectivity and energization of a grid node or asset, one row per valid combination: connected_energized (normal), connected_deenergized (upstream outage), islanded (energized from local sources), disconnected';
COMMENT ON COLUMN topology.grid_states.grid_state_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.grid_states.internal_code IS 'Stable machine code: connected_energized, connected_deenergized, islanded, disconnected. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.grid_states.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';
COMMENT ON COLUMN topology.grid_states.is_grid_connected IS 'TRUE when connected to the utility grid. [BOOLEAN; NOT NULL]';
COMMENT ON COLUMN topology.grid_states.is_energized IS 'TRUE when energized, from the grid or, when islanded, from local sources. [BOOLEAN; NOT NULL]';

-- Lookup ids by internal_code: column defaults below and module code use them, so seeds never rely on identity values.
CREATE OR REPLACE FUNCTION topology.asset_lifecycle_state_id(code text) RETURNS integer LANGUAGE sql STABLE AS
    $$ SELECT asset_lifecycle_state_id FROM topology.asset_lifecycle_states WHERE internal_code = code $$;
CREATE OR REPLACE FUNCTION topology.participation_state_id(code text) RETURNS integer LANGUAGE sql STABLE AS
    $$ SELECT participation_state_id FROM topology.participation_states WHERE internal_code = code $$;


-- =====================================================================
-- topology: grid node identity
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.grid_nodes
(
    grid_node_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_type_id   INTEGER NOT NULL REFERENCES topology.grid_node_types (grid_node_type_id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);
COMMENT ON TABLE topology.grid_nodes IS 'identity registry for every grid node: one id space across substations, feeders, neighborhood transformers, premises, and microgrids. A row is created by trigger when a node is inserted and is never changed or deleted, so node-keyed data keeps a stable reference after the node itself is removed. Not versioned: its rows never change';
COMMENT ON COLUMN topology.grid_nodes.grid_node_id IS 'Grid node id, unique across all node tables. Referenced by node-keyed data (operating envelopes, node measurements, the future network model). [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.grid_nodes.grid_node_type_id IS 'Which node table the node lives in. [INTEGER; NOT NULL; FK topology.grid_node_types(grid_node_type_id)]';
COMMENT ON COLUMN topology.grid_nodes.created_at IS 'When the node was registered. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';


-- =====================================================================
-- topology: hardware profiles (equipment models)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.hardware_profiles
(
    hardware_profile_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    hardware_profile_code   VARCHAR(50) NOT NULL UNIQUE,
    manufacturer            VARCHAR(100) NOT NULL,
    model                   VARCHAR(100) NOT NULL,
    technology_type_id      INTEGER NULL REFERENCES topology.technology_types (technology_type_id),
    rated_kw                DOUBLE PRECISION NULL CHECK (rated_kw > 0),
    rated_kva               DOUBLE PRECISION NULL CHECK (rated_kva > 0),
    rated_kwh               DOUBLE PRECISION NULL CHECK (rated_kwh > 0),
    round_trip_efficiency   DOUBLE PRECISION NULL CHECK (round_trip_efficiency > 0 AND round_trip_efficiency <= 1),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    CONSTRAINT uk_hardware_profile_technology UNIQUE (hardware_profile_id, technology_type_id)  -- target for the component technology match
);
COMMENT ON TABLE topology.hardware_profiles IS 'manufacturer equipment models with per-unit nameplate specs, used by asset components (physics) and assets (control/integration routing)';
COMMENT ON COLUMN topology.hardware_profiles.hardware_profile_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK; UNIQUE (hardware_profile_id, technology_type_id)]';
COMMENT ON COLUMN topology.hardware_profiles.hardware_profile_code IS 'Stable natural key for seeding and external references, ex. tesla_powerwall_3. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.hardware_profiles.manufacturer IS 'Equipment manufacturer. Selects the device-integration adapter for assets controlled through this equipment. [VARCHAR(100); NOT NULL]';
COMMENT ON COLUMN topology.hardware_profiles.model IS 'Manufacturer model name, ex. Powerwall 3. [VARCHAR(100); NOT NULL]';
COMMENT ON COLUMN topology.hardware_profiles.technology_type_id IS 'Energy technology of the equipment. NULL = conversion or control equipment (inverter, gateway, plant controller), which asset components cannot use. [INTEGER; NULL; FK topology.technology_types(technology_type_id); UNIQUE (hardware_profile_id, technology_type_id)]';
COMMENT ON COLUMN topology.hardware_profiles.rated_kw IS 'Per-unit rated real power in kW: battery continuous power, PV module kW-dc, or inverter kW-ac. NULL = not applicable. [DOUBLE PRECISION; NULL; CHECK > 0]';
COMMENT ON COLUMN topology.hardware_profiles.rated_kva IS 'Per-unit inverter apparent power rating in kVA. NULL = not applicable. [DOUBLE PRECISION; NULL; CHECK > 0]';
COMMENT ON COLUMN topology.hardware_profiles.rated_kwh IS 'Per-unit usable energy capacity in kWh, storage only. NULL = not storage. [DOUBLE PRECISION; NULL; CHECK > 0]';
COMMENT ON COLUMN topology.hardware_profiles.round_trip_efficiency IS 'Storage round-trip efficiency as a fraction, ex. 0.90. NULL = not storage or unknown. [DOUBLE PRECISION; NULL; CHECK > 0 and <= 1]';


-- =====================================================================
-- topology: hierarchy (substation -> feeder -> neighborhood transformer -> premise)
-- Hierarchy foreign keys record the normal (as-designed) configuration, not the energized path.
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.substations
(
    substation_id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id            BIGINT NOT NULL UNIQUE REFERENCES topology.grid_nodes (grid_node_id),
    substation_code         VARCHAR(50) NOT NULL UNIQUE,
    rated_capacity_kva      DOUBLE PRECISION NOT NULL CHECK (rated_capacity_kva > 0),
    location                GEOGRAPHY(Point, 4326),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.substations IS 'a distribution substation: the top of the grid hierarchy. Feeders, microgrids, and utility-scale assets connect to it';
COMMENT ON COLUMN topology.substations.substation_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.substations.grid_node_id IS 'This node''s id in the shared grid node id space. Assigned by trigger (topology.assign_grid_node) on insert and never changes; client values are ignored. [BIGINT; NOT NULL; UNIQUE; FK topology.grid_nodes(grid_node_id)]';
COMMENT ON COLUMN topology.substations.substation_code IS 'Stable natural key for seeding and external references, ex. PDX-SUB-ARBOR. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.substations.rated_capacity_kva IS 'Transformer nameplate capacity in kVA. Dispatch must keep the substation''s net flow within it. [DOUBLE PRECISION; NOT NULL; CHECK > 0]';
COMMENT ON COLUMN topology.substations.location IS 'Site location (WGS 84). NULL = unknown. [GEOGRAPHY(Point, 4326); NULL]';

CREATE TABLE IF NOT EXISTS topology.feeders
(
    feeder_id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id            BIGINT NOT NULL UNIQUE REFERENCES topology.grid_nodes (grid_node_id),
    substation_id           BIGINT NOT NULL REFERENCES topology.substations (substation_id),
    feeder_code             VARCHAR(50) NOT NULL UNIQUE,
    nominal_voltage_kv      DOUBLE PRECISION NOT NULL CHECK (nominal_voltage_kv > 0),
    rated_capacity_kva      DOUBLE PRECISION NULL CHECK (rated_capacity_kva > 0),
    line_geometry           GEOMETRY(LineString, 4326),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.feeders IS 'a distribution circuit leaving a substation at primary voltage. Serves neighborhood transformers, primary-service premises, microgrids, and feeder-connected assets';
COMMENT ON COLUMN topology.feeders.feeder_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.feeders.grid_node_id IS 'This node''s id in the shared grid node id space. Assigned by trigger (topology.assign_grid_node) on insert and never changes; client values are ignored. [BIGINT; NOT NULL; UNIQUE; FK topology.grid_nodes(grid_node_id)]';
COMMENT ON COLUMN topology.feeders.substation_id IS 'Substation that normally serves this feeder. [BIGINT; NOT NULL; FK topology.substations(substation_id)]';
COMMENT ON COLUMN topology.feeders.feeder_code IS 'Stable natural key for seeding and external references. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.feeders.nominal_voltage_kv IS 'Nominal line-to-line primary voltage in kV, ex. 12.47. [DOUBLE PRECISION; NOT NULL; CHECK > 0]';
COMMENT ON COLUMN topology.feeders.rated_capacity_kva IS 'Thermal capacity of the circuit in kVA. NULL = not modeled. [DOUBLE PRECISION; NULL; CHECK > 0]';
COMMENT ON COLUMN topology.feeders.line_geometry IS 'Route of the circuit (WGS 84). NULL = unknown. [GEOMETRY(LineString, 4326); NULL]';
CREATE INDEX IF NOT EXISTS ix_feeders_substation_id ON topology.feeders (substation_id);

CREATE TABLE IF NOT EXISTS topology.neighborhood_transformers
(
    neighborhood_transformer_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id                    BIGINT NOT NULL UNIQUE REFERENCES topology.grid_nodes (grid_node_id),
    feeder_id                       BIGINT NOT NULL REFERENCES topology.feeders (feeder_id),
    neighborhood_transformer_code   VARCHAR(50) NOT NULL UNIQUE,
    rated_capacity_kva              DOUBLE PRECISION NOT NULL CHECK (rated_capacity_kva > 0),
    secondary_voltage_v             DOUBLE PRECISION NOT NULL CHECK (secondary_voltage_v > 0),
    location                        GEOGRAPHY(Point, 4326),
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.neighborhood_transformers IS 'a distribution (service) transformer that steps feeder primary voltage down to the secondary voltage its premises are served at';
COMMENT ON COLUMN topology.neighborhood_transformers.neighborhood_transformer_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.neighborhood_transformers.grid_node_id IS 'This node''s id in the shared grid node id space. Assigned by trigger (topology.assign_grid_node) on insert and never changes; client values are ignored. [BIGINT; NOT NULL; UNIQUE; FK topology.grid_nodes(grid_node_id)]';
COMMENT ON COLUMN topology.neighborhood_transformers.feeder_id IS 'Feeder that normally serves this transformer. [BIGINT; NOT NULL; FK topology.feeders(feeder_id)]';
COMMENT ON COLUMN topology.neighborhood_transformers.neighborhood_transformer_code IS 'Stable natural key for seeding and external references. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.neighborhood_transformers.rated_capacity_kva IS 'Nameplate capacity in kVA, ex. 50. Dispatch must keep the net flow of the premises and assets it serves within it. [DOUBLE PRECISION; NOT NULL; CHECK > 0]';
COMMENT ON COLUMN topology.neighborhood_transformers.secondary_voltage_v IS 'Secondary (customer-side) voltage in V, ex. 240. [DOUBLE PRECISION; NOT NULL; CHECK > 0]';
COMMENT ON COLUMN topology.neighborhood_transformers.location IS 'Transformer location (WGS 84). NULL = unknown. [GEOGRAPHY(Point, 4326); NULL]';
CREATE INDEX IF NOT EXISTS ix_neighborhood_transformers_feeder_id ON topology.neighborhood_transformers (feeder_id);

CREATE TABLE IF NOT EXISTS topology.premises
(
    premise_id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id                    BIGINT NOT NULL UNIQUE REFERENCES topology.grid_nodes (grid_node_id),
    -- service point: exactly one of
    neighborhood_transformer_id     BIGINT NULL REFERENCES topology.neighborhood_transformers (neighborhood_transformer_id),
    feeder_id                       BIGINT NULL REFERENCES topology.feeders (feeder_id),
    premise_code                    VARCHAR(50) NOT NULL UNIQUE,
    customer_segment_id             INTEGER NOT NULL REFERENCES topology.customer_segments (customer_segment_id),
    service_capacity_kva            DOUBLE PRECISION NULL CHECK (service_capacity_kva > 0),
    export_limit_kw                 DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),
    can_island                      BOOLEAN NOT NULL DEFAULT FALSE,
    address                         VARCHAR(255),
    location                        GEOGRAPHY(Point, 4326),
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    CONSTRAINT ck_premises_single_connection CHECK (num_nonnulls(neighborhood_transformer_id, feeder_id) = 1)
);
COMMENT ON TABLE topology.premises IS 'a customer site behind one utility meter (point of common coupling), served from a neighborhood transformer (secondary) or directly from a feeder (primary). All assets at a premise island together when it islands';
COMMENT ON COLUMN topology.premises.premise_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.premises.grid_node_id IS 'This node''s id in the shared grid node id space. Assigned by trigger (topology.assign_grid_node) on insert and never changes; client values are ignored. [BIGINT; NOT NULL; UNIQUE; FK topology.grid_nodes(grid_node_id)]';
COMMENT ON COLUMN topology.premises.neighborhood_transformer_id IS 'Transformer serving the premise at secondary voltage (most premises). [BIGINT; NULL; FK topology.neighborhood_transformers(neighborhood_transformer_id); CHECK exactly one of neighborhood_transformer_id, feeder_id is set]';
COMMENT ON COLUMN topology.premises.feeder_id IS 'Feeder serving the premise at primary voltage: the customer owns its transformer, ex. a large C&I site. [BIGINT; NULL; FK topology.feeders(feeder_id); CHECK exactly one of neighborhood_transformer_id, feeder_id is set]';
COMMENT ON COLUMN topology.premises.premise_code IS 'Stable natural key for seeding and external references. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.premises.customer_segment_id IS 'Utility service class of the premise. [INTEGER; NOT NULL; FK topology.customer_segments(customer_segment_id)]';
COMMENT ON COLUMN topology.premises.service_capacity_kva IS 'Site import limit set by the service size, in kVA, ex. 200 A at 240 V = 48 kVA. NULL = unknown. [DOUBLE PRECISION; NULL; CHECK > 0]';
COMMENT ON COLUMN topology.premises.export_limit_kw IS 'Site export limit at the meter from the interconnection agreement, in kW. 0 = non-export, NULL = no limit recorded. [DOUBLE PRECISION; NULL; CHECK >= 0]';
COMMENT ON COLUMN topology.premises.can_island IS 'TRUE when a backup gateway or site microgrid controller lets the premise island (a behind-the-meter microgrid). [BOOLEAN; NOT NULL; DEFAULT FALSE]';
COMMENT ON COLUMN topology.premises.address IS 'Service address. NULL = unknown. [VARCHAR(255); NULL]';
COMMENT ON COLUMN topology.premises.location IS 'Meter location (WGS 84). NULL = unknown. [GEOGRAPHY(Point, 4326); NULL]';
CREATE INDEX IF NOT EXISTS ix_premises_neighborhood_transformer_id ON topology.premises (neighborhood_transformer_id);
CREATE INDEX IF NOT EXISTS ix_premises_feeder_id ON topology.premises (feeder_id);


-- =====================================================================
-- topology: microgrids and assets
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.microgrids
(
    microgrid_id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id            BIGINT NOT NULL UNIQUE REFERENCES topology.grid_nodes (grid_node_id),
    microgrid_code          VARCHAR(50) NOT NULL UNIQUE,
    feeder_id               BIGINT NULL REFERENCES topology.feeders (feeder_id),
    substation_id           BIGINT NULL REFERENCES topology.substations (substation_id),
    can_island              BOOLEAN NOT NULL DEFAULT TRUE,
    import_limit_kw         DOUBLE PRECISION NULL CHECK (import_limit_kw >= 0),
    export_limit_kw         DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    CONSTRAINT ck_microgrids_single_connection CHECK (num_nonnulls(feeder_id, substation_id) = 1)
);
COMMENT ON TABLE topology.microgrids IS 'utility-side (FTM) microgrid spanning several premises and/or feeder-connected assets. A behind-the-meter microgrid is represented by its premise (can_island and site limits)';
COMMENT ON COLUMN topology.microgrids.microgrid_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.microgrids.grid_node_id IS 'This node''s id in the shared grid node id space. Assigned by trigger (topology.assign_grid_node) on insert and never changes; client values are ignored. [BIGINT; NOT NULL; UNIQUE; FK topology.grid_nodes(grid_node_id)]';
COMMENT ON COLUMN topology.microgrids.microgrid_code IS 'Stable natural key for seeding and external references. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.microgrids.feeder_id IS 'Feeder the microgrid''s point of common coupling is on. [BIGINT; NULL; FK topology.feeders(feeder_id); CHECK exactly one of feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.microgrids.substation_id IS 'Substation the microgrid''s point of common coupling is at. [BIGINT; NULL; FK topology.substations(substation_id); CHECK exactly one of feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.microgrids.can_island IS 'TRUE when the microgrid can disconnect at its point of common coupling and run islanded. [BOOLEAN; NOT NULL; DEFAULT TRUE]';
COMMENT ON COLUMN topology.microgrids.import_limit_kw IS 'Import limit at the point of common coupling in kW. NULL = no limit recorded. [DOUBLE PRECISION; NULL; CHECK >= 0]';
COMMENT ON COLUMN topology.microgrids.export_limit_kw IS 'Export limit at the point of common coupling in kW. 0 = non-export, NULL = no limit recorded. [DOUBLE PRECISION; NULL; CHECK >= 0]';
CREATE INDEX IF NOT EXISTS ix_microgrids_feeder_id ON topology.microgrids (feeder_id);
CREATE INDEX IF NOT EXISTS ix_microgrids_substation_id ON topology.microgrids (substation_id);

CREATE TABLE IF NOT EXISTS topology.assets
(
    asset_id                        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_code                      VARCHAR(50) NOT NULL UNIQUE,
    -- connection point: exactly one level of the hierarchy
    premise_id                      BIGINT NULL REFERENCES topology.premises (premise_id),
    neighborhood_transformer_id     BIGINT NULL REFERENCES topology.neighborhood_transformers (neighborhood_transformer_id),
    feeder_id                       BIGINT NULL REFERENCES topology.feeders (feeder_id),
    substation_id                   BIGINT NULL REFERENCES topology.substations (substation_id),
    microgrid_id                    BIGINT NULL REFERENCES topology.microgrids (microgrid_id),
    interconnection_type_id         INTEGER NOT NULL REFERENCES topology.interconnection_types (interconnection_type_id),
    control_hardware_profile_id     INTEGER NULL REFERENCES topology.hardware_profiles (hardware_profile_id),
    -- status: physical lifecycle (with field dates) and program participation; new assets cannot be dispatched
    asset_lifecycle_state_id        INTEGER NOT NULL DEFAULT topology.asset_lifecycle_state_id('planned')
                                        REFERENCES topology.asset_lifecycle_states (asset_lifecycle_state_id),
    participation_state_id          INTEGER NOT NULL DEFAULT topology.participation_state_id('not_enrolled')
                                        REFERENCES topology.participation_states (participation_state_id),
    in_service_date                 DATE NULL,
    decommissioned_date             DATE NULL,
    -- ratings at the connection point (what dispatch uses)
    rated_p_min_kw                  DOUBLE PRECISION NOT NULL,
    rated_p_max_kw                  DOUBLE PRECISION NOT NULL,
    rated_kva                       DOUBLE PRECISION NOT NULL CHECK (rated_kva > 0),
    export_limit_kw                 DOUBLE PRECISION NULL CHECK (export_limit_kw >= 0),
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    CONSTRAINT ck_assets_single_connection CHECK (num_nonnulls(premise_id, neighborhood_transformer_id, feeder_id, substation_id) = 1),
    CONSTRAINT ck_assets_power_bounds      CHECK (rated_p_min_kw <= rated_p_max_kw),
    CONSTRAINT ck_assets_power_within_kva  CHECK (GREATEST(ABS(rated_p_min_kw), ABS(rated_p_max_kw)) <= rated_kva),
    CONSTRAINT ck_assets_service_dates     CHECK (decommissioned_date IS NULL OR in_service_date IS NULL OR decommissioned_date >= in_service_date)
);
COMMENT ON TABLE topology.assets IS 'a grid-facing controllable resource: everything behind one control interface at one connection point. Makeup (PV only, storage, PV + storage) and energy capacity are derived from asset_components. Assets are decommissioned, not deleted: DELETE only corrects a record entered in error and needs a change reason (topology.check_asset_lifecycle)';
COMMENT ON COLUMN topology.assets.asset_id IS 'Surrogate key. Other modules refer to assets by this id or by asset_code. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.assets.asset_code IS 'Stable natural key for seeding and external references; simulation configuration refers to assets by it. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN topology.assets.premise_id IS 'Premise the asset connects at, behind or across its meter. [BIGINT; NULL; FK topology.premises(premise_id); CHECK exactly one of premise_id, neighborhood_transformer_id, feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.assets.neighborhood_transformer_id IS 'Transformer secondary the asset connects to in front of any meter, ex. a utility pole-mounted battery. [BIGINT; NULL; FK topology.neighborhood_transformers(neighborhood_transformer_id); CHECK exactly one of premise_id, neighborhood_transformer_id, feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.assets.feeder_id IS 'Feeder the asset connects to directly, in front of any meter. [BIGINT; NULL; FK topology.feeders(feeder_id); CHECK exactly one of premise_id, neighborhood_transformer_id, feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.assets.substation_id IS 'Substation the asset connects at, in front of any meter. [BIGINT; NULL; FK topology.substations(substation_id); CHECK exactly one of premise_id, neighborhood_transformer_id, feeder_id, substation_id is set]';
COMMENT ON COLUMN topology.assets.microgrid_id IS 'Utility-side microgrid the asset belongs to; membership only, the connection point is one of the columns above. NULL = none. [BIGINT; NULL; FK topology.microgrids(microgrid_id)]';
COMMENT ON COLUMN topology.assets.interconnection_type_id IS 'Meter position of the asset. Assets not connected at a premise are always ftm (enforced in the Asset entity). [INTEGER; NOT NULL; FK topology.interconnection_types(interconnection_type_id)]';
COMMENT ON COLUMN topology.assets.control_hardware_profile_id IS 'Controller that commands go through; its manufacturer selects the integration adapter. NULL = not recorded. [INTEGER; NULL; FK topology.hardware_profiles(hardware_profile_id)]';
COMMENT ON COLUMN topology.assets.asset_lifecycle_state_id IS 'Physical lifecycle: planned, installed, commissioned (in service), or decommissioned. New assets start planned. Decommissioned is terminal and keeps the row, so the code stays reserved. Rules on dates and participation are enforced by trigger (topology.check_asset_lifecycle). [INTEGER; NOT NULL; DEFAULT planned; FK topology.asset_lifecycle_states(asset_lifecycle_state_id)]';
COMMENT ON COLUMN topology.assets.participation_state_id IS 'Whether the VPP may use the asset: not_enrolled, active, suspended, or opted_out. New assets start not_enrolled; only a commissioned asset can be active or suspended. [INTEGER; NOT NULL; DEFAULT not_enrolled; FK topology.participation_states(participation_state_id)]';
COMMENT ON COLUMN topology.assets.in_service_date IS 'Date the asset entered service in the field (permission to operate), not when it was recorded in RVA, so past periods can be replayed with the assets that existed then. Required when commissioned. NULL = not yet in service, or decommissioned without ever entering service. [DATE; NULL; CHECK <= decommissioned_date]';
COMMENT ON COLUMN topology.assets.decommissioned_date IS 'Date the asset left service in the field. Set exactly when the asset is decommissioned. [DATE; NULL; CHECK >= in_service_date]';
COMMENT ON COLUMN topology.assets.rated_p_min_kw IS 'Minimum real power at the connection point in kW; negative = charging or import. What dispatch uses, not the sum of components. [DOUBLE PRECISION; NOT NULL; CHECK <= rated_p_max_kw, and its magnitude <= rated_kva]';
COMMENT ON COLUMN topology.assets.rated_p_max_kw IS 'Maximum real power at the connection point in kW; positive = discharging or export. What dispatch uses, not the sum of components. [DOUBLE PRECISION; NOT NULL; CHECK >= rated_p_min_kw, and its magnitude <= rated_kva]';
COMMENT ON COLUMN topology.assets.rated_kva IS 'Apparent power rating at the connection point in kVA; bounds the magnitude of both real power ratings. [DOUBLE PRECISION; NOT NULL; CHECK > 0, and >= the magnitude of rated_p_min_kw and rated_p_max_kw]';
COMMENT ON COLUMN topology.assets.export_limit_kw IS 'Per-system export limit at the point of DER connection in kW; site limits are on premises and microgrids. NULL = no limit beyond rated_p_max_kw. [DOUBLE PRECISION; NULL; CHECK >= 0]';
CREATE INDEX IF NOT EXISTS ix_assets_premise_id    ON topology.assets (premise_id);
CREATE INDEX IF NOT EXISTS ix_assets_neighborhood_transformer_id ON topology.assets (neighborhood_transformer_id);
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
    rated_kw                DOUBLE PRECISION NULL CHECK (rated_kw >= 0),
    rated_kwh               DOUBLE PRECISION NULL CHECK (rated_kwh >= 0),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    -- a component's profile must be an energy product of the same technology (rejects control-only equipment)
    CONSTRAINT fk_asset_components_hardware_profile FOREIGN KEY (hardware_profile_id, technology_type_id)
        REFERENCES topology.hardware_profiles (hardware_profile_id, technology_type_id)
);
COMMENT ON TABLE topology.asset_components IS 'what an asset is made of, for simulation. Components are never dispatched directly';
COMMENT ON COLUMN topology.asset_components.asset_component_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.asset_components.asset_id IS 'Asset the component belongs to. Components are deleted with their asset. [BIGINT; NOT NULL; FK topology.assets(asset_id) ON DELETE CASCADE]';
COMMENT ON COLUMN topology.asset_components.technology_type_id IS 'Technology of the component. When a hardware profile is set, it must be of the same technology. [INTEGER; NOT NULL; FK topology.technology_types(technology_type_id); FK topology.hardware_profiles(hardware_profile_id, technology_type_id)]';
COMMENT ON COLUMN topology.asset_components.hardware_profile_id IS 'Equipment model of the component; must be an energy product of the component''s technology, so control-only equipment is rejected. NULL = generic equipment. [INTEGER; NULL; FK topology.hardware_profiles(hardware_profile_id, technology_type_id)]';
COMMENT ON COLUMN topology.asset_components.unit_count IS 'Number of identical units, ex. 2 battery modules. [INTEGER; NOT NULL; DEFAULT 1; CHECK > 0]';
COMMENT ON COLUMN topology.asset_components.rated_kw IS 'Installed total real power in kW: PV kW-dc or battery power. NULL = not recorded. [DOUBLE PRECISION; NULL; CHECK >= 0]';
COMMENT ON COLUMN topology.asset_components.rated_kwh IS 'Installed total usable energy capacity in kWh, storage only. NULL = not storage or not recorded. [DOUBLE PRECISION; NULL; CHECK >= 0]';
CREATE INDEX IF NOT EXISTS ix_asset_components_asset_id ON topology.asset_components (asset_id);


-- =====================================================================
-- topology: grid node identity triggers
-- =====================================================================

-- Gives each new grid node a grid_node_id in topology.grid_nodes and keeps it fixed for the life of the row.
-- TG_ARGV[0] is the node's grid_node_types.internal_code. Like created_at, a client-supplied value is overwritten.
-- Fires before trg_<table>_versioning (triggers fire in name order), so a no-op change to grid_node_id writes no history.
CREATE OR REPLACE FUNCTION topology.assign_grid_node() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.grid_node_id := NULL;
        INSERT INTO topology.grid_nodes (grid_node_type_id)
        SELECT grid_node_type_id FROM topology.grid_node_types WHERE internal_code = TG_ARGV[0]
        RETURNING grid_node_id INTO NEW.grid_node_id;
        IF NEW.grid_node_id IS NULL THEN
            RAISE EXCEPTION 'unknown grid node type %', TG_ARGV[0];
        END IF;
    ELSE
        NEW.grid_node_id := OLD.grid_node_id;   -- a node's identity is immutable
    END IF;
    RETURN NEW;
END $$;

CREATE OR REPLACE TRIGGER trg_substations_grid_node BEFORE INSERT OR UPDATE ON topology.substations
    FOR EACH ROW EXECUTE FUNCTION topology.assign_grid_node('substation');
CREATE OR REPLACE TRIGGER trg_feeders_grid_node BEFORE INSERT OR UPDATE ON topology.feeders
    FOR EACH ROW EXECUTE FUNCTION topology.assign_grid_node('feeder');
CREATE OR REPLACE TRIGGER trg_neighborhood_transformers_grid_node BEFORE INSERT OR UPDATE ON topology.neighborhood_transformers
    FOR EACH ROW EXECUTE FUNCTION topology.assign_grid_node('neighborhood_transformer');
CREATE OR REPLACE TRIGGER trg_premises_grid_node BEFORE INSERT OR UPDATE ON topology.premises
    FOR EACH ROW EXECUTE FUNCTION topology.assign_grid_node('premise');
CREATE OR REPLACE TRIGGER trg_microgrids_grid_node BEFORE INSERT OR UPDATE ON topology.microgrids
    FOR EACH ROW EXECUTE FUNCTION topology.assign_grid_node('microgrid');

-- The registry is append-only: its rows are never changed or deleted.
CREATE OR REPLACE FUNCTION topology.reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '%.% is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME;
END $$;
CREATE OR REPLACE TRIGGER trg_grid_nodes_append_only
    BEFORE UPDATE OR DELETE ON topology.grid_nodes
    FOR EACH ROW EXECUTE FUNCTION topology.reject_change();


-- =====================================================================
-- topology: asset lifecycle rules
-- =====================================================================

-- Keeps lifecycle state, field dates, and participation consistent, makes decommissioned terminal, and limits DELETE to
-- corrections: a decommissioned asset is kept for audit and settlement, and any delete needs SET LOCAL rva.change_reason.
-- Fires before trg_assets_versioning (triggers fire in name order), so a rejected change writes no history.
CREATE OR REPLACE FUNCTION topology.check_asset_lifecycle() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    old_state       text;
    new_state       text;
    participation   text;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        SELECT internal_code INTO old_state FROM topology.asset_lifecycle_states WHERE asset_lifecycle_state_id = OLD.asset_lifecycle_state_id;
    END IF;

    IF TG_OP = 'DELETE' THEN
        IF old_state = 'decommissioned' THEN
            RAISE EXCEPTION 'asset % is decommissioned and cannot be deleted: decommissioned assets are kept for audit and settlement', OLD.asset_code;
        END IF;
        IF coalesce(current_setting('rva.change_reason', true), '') = '' THEN
            RAISE EXCEPTION 'deleting asset % needs SET LOCAL rva.change_reason: DELETE only corrects a record entered in error; decommission by changing asset_lifecycle_state_id', OLD.asset_code;
        END IF;
        RETURN OLD;
    END IF;

    SELECT internal_code INTO new_state FROM topology.asset_lifecycle_states WHERE asset_lifecycle_state_id = NEW.asset_lifecycle_state_id;
    SELECT internal_code INTO participation FROM topology.participation_states WHERE participation_state_id = NEW.participation_state_id;

    IF old_state = 'decommissioned' AND new_state <> 'decommissioned' THEN
        RAISE EXCEPTION 'asset % is decommissioned, which is terminal', NEW.asset_code USING ERRCODE = 'check_violation';
    END IF;
    IF new_state IN ('planned', 'installed') AND NEW.in_service_date IS NOT NULL THEN
        RAISE EXCEPTION 'asset % is %, so it has no in-service date', NEW.asset_code, new_state USING ERRCODE = 'check_violation';
    END IF;
    IF new_state = 'commissioned' AND NEW.in_service_date IS NULL THEN
        RAISE EXCEPTION 'commissioned asset % needs an in_service_date', NEW.asset_code USING ERRCODE = 'check_violation';
    END IF;
    IF (new_state = 'decommissioned') <> (NEW.decommissioned_date IS NOT NULL) THEN
        RAISE EXCEPTION 'asset %: decommissioned_date is set exactly when the asset is decommissioned', NEW.asset_code USING ERRCODE = 'check_violation';
    END IF;
    IF participation IN ('active', 'suspended') AND new_state <> 'commissioned' THEN
        RAISE EXCEPTION 'asset % is %: only a commissioned asset can be % in a program', NEW.asset_code, new_state, participation USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_assets_lifecycle BEFORE INSERT OR UPDATE OR DELETE ON topology.assets
    FOR EACH ROW EXECUTE FUNCTION topology.check_asset_lifecycle();


-- =====================================================================
-- topology: grid state events (connectivity and energization)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.grid_state_events
(
    grid_state_event_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grid_node_id            BIGINT NULL REFERENCES topology.grid_nodes (grid_node_id),
    asset_id                BIGINT NULL REFERENCES topology.assets (asset_id),
    grid_state_id           INTEGER NOT NULL REFERENCES topology.grid_states (grid_state_id),
    occurred_at             TIMESTAMPTZ NOT NULL,
    recorded_at             TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    reason                  VARCHAR(500) NULL,
    actor_id                UUID NULL,
    actor_type              VARCHAR(20) NULL,
    details                 JSONB NULL,
    CONSTRAINT ck_grid_state_events_single_target CHECK (num_nonnulls(grid_node_id, asset_id) = 1),
    CONSTRAINT ck_grid_state_events_not_future    CHECK (occurred_at <= recorded_at)
);
COMMENT ON TABLE topology.grid_state_events IS 'append-only history of the connectivity and energization state of grid nodes and assets, ex. a feeder trip, a microgrid islanding, an asset disconnect. A target with no event is in its normal state (connected_energized). Read the state at a point in time through topology.latest_grid_states; effective asset state is derived from the path by the GridTopology module';
COMMENT ON COLUMN topology.grid_state_events.grid_state_event_id IS 'Surrogate key; increases with insertion order. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.grid_state_events.grid_node_id IS 'Grid node whose state changed: a substation, feeder, transformer, premise, or microgrid. [BIGINT; NULL; FK topology.grid_nodes(grid_node_id); CHECK exactly one of grid_node_id, asset_id is set]';
COMMENT ON COLUMN topology.grid_state_events.asset_id IS 'Asset whose own state changed, ex. its disconnect opened. An asset with events cannot be deleted. [BIGINT; NULL; FK topology.assets(asset_id); CHECK exactly one of grid_node_id, asset_id is set]';
COMMENT ON COLUMN topology.grid_state_events.grid_state_id IS 'State the target entered. [INTEGER; NOT NULL; FK topology.grid_states(grid_state_id)]';
COMMENT ON COLUMN topology.grid_state_events.occurred_at IS 'When the change happened in the field, as reported; may be earlier than recorded_at for late reports. [TIMESTAMPTZ; NOT NULL; CHECK <= recorded_at]';
COMMENT ON COLUMN topology.grid_state_events.recorded_at IS 'When RVA recorded the event. Set by trigger; client values are overwritten. As-of reads only see events recorded by then, so they are reproducible. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp(); CHECK >= occurred_at]';
COMMENT ON COLUMN topology.grid_state_events.reason IS 'Why the state changed, ex. feeder breaker trip; from the row or SET LOCAL rva.change_reason. NULL = not given. [VARCHAR(500); NULL]';
COMMENT ON COLUMN topology.grid_state_events.actor_id IS 'Who or what reported the change; from SET LOCAL rva.actor. NULL = unattributed. [UUID; NULL; REF a user or service id (UUIDv7) owned by the Identity module (Phase 3), no FK]';
COMMENT ON COLUMN topology.grid_state_events.actor_type IS 'Kind of actor: user, service, or system; from SET LOCAL rva.actor_type. NULL = not given. [VARCHAR(20); NULL]';
COMMENT ON COLUMN topology.grid_state_events.details IS 'Additional event data as JSON, ex. the SCADA point or telemetry message. NULL = none. [JSONB; NULL]';
CREATE INDEX IF NOT EXISTS ix_grid_state_events_grid_node ON topology.grid_state_events (grid_node_id, occurred_at);
CREATE INDEX IF NOT EXISTS ix_grid_state_events_asset ON topology.grid_state_events (asset_id, occurred_at);

-- Stamps recorded_at and fills actor and reason from the transaction's SET LOCAL rva.* settings.
CREATE OR REPLACE FUNCTION topology.record_grid_state_event() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.recorded_at := clock_timestamp();
    NEW.reason      := coalesce(NEW.reason, NULLIF(current_setting('rva.change_reason', true), ''));
    NEW.actor_id    := coalesce(NEW.actor_id, NULLIF(current_setting('rva.actor', true), '')::uuid);
    NEW.actor_type  := coalesce(NEW.actor_type, NULLIF(current_setting('rva.actor_type', true), ''));
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_grid_state_events_record BEFORE INSERT ON topology.grid_state_events
    FOR EACH ROW EXECUTE FUNCTION topology.record_grid_state_event();
CREATE OR REPLACE TRIGGER trg_grid_state_events_append_only BEFORE UPDATE OR DELETE ON topology.grid_state_events
    FOR EACH ROW EXECUTE FUNCTION topology.reject_change();

-- The latest state of every node and asset that has events, as known at a point in time: events that happened and
-- were recorded by then. Targets with no row are in their normal state (connected_energized).
CREATE OR REPLACE FUNCTION topology.latest_grid_states(point_in_time timestamptz)
RETURNS TABLE (grid_node_id bigint, asset_id bigint, grid_state_id integer, occurred_at timestamptz)
LANGUAGE sql STABLE AS $$
    SELECT DISTINCT ON (e.grid_node_id, e.asset_id) e.grid_node_id, e.asset_id, e.grid_state_id, e.occurred_at
    FROM topology.grid_state_events e
    WHERE e.occurred_at <= point_in_time AND e.recorded_at <= point_in_time
    ORDER BY e.grid_node_id, e.asset_id, e.occurred_at DESC, e.grid_state_event_id DESC
$$;


-- =====================================================================
-- topology: system versioning
-- =====================================================================

-- Archives the outgoing row version into <table>_history and stamps the new one.
-- The application never sets created_at or sys_period; values it supplies are overwritten.
CREATE OR REPLACE FUNCTION topology.version_row() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    changed_at  timestamptz := clock_timestamp();  -- not now(): several changes in one transaction must stay ordered
    archived    record;
    cols        text;
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.created_at := changed_at;
        NEW.sys_period := tstzrange(changed_at, NULL, '[)');
        RETURN NEW;
    END IF;

    IF TG_OP = 'UPDATE' THEN
        NEW.created_at := OLD.created_at;   -- creation time is immutable
        NEW.sys_period := OLD.sys_period;   -- ignore client-supplied periods for the comparison below
        IF NEW IS NOT DISTINCT FROM OLD THEN
            RETURN NEW;                     -- no-op update: nothing to archive
        END IF;
    END IF;

    archived := OLD;
    archived.sys_period := tstzrange(lower(OLD.sys_period), changed_at, '[)');

    -- Insert by column name, not position: a column added to a table later is appended to its history table
    -- after the audit columns, so the two column orders differ.
    SELECT string_agg(format('%I', attname), ', ' ORDER BY attnum) INTO cols
    FROM pg_attribute
    WHERE attrelid = TG_RELID AND attnum > 0 AND NOT attisdropped;

    EXECUTE format('INSERT INTO %I.%I (%s, operation, changed_by, actor_type, change_reason) SELECT ($1).*, $2, $3, $4, $5',
                   TG_TABLE_SCHEMA, TG_TABLE_NAME || '_history', cols)
        USING archived,
              TG_OP,
              NULLIF(current_setting('rva.actor', true), '')::uuid,
              NULLIF(current_setting('rva.actor_type', true), ''),
              NULLIF(current_setting('rva.change_reason', true), '');

    IF TG_OP = 'UPDATE' THEN
        NEW.sys_period := tstzrange(changed_at, NULL, '[)');
        RETURN NEW;
    END IF;
    RETURN OLD;
END $$;

-- Fails if topology.<t> is not correctly versioned: its history table must have every main-table column with the
-- same type (matched by name; order does not matter) plus the audit columns, and the versioning trigger must exist.
-- A history table may keep columns the main table has since dropped, so old versions keep their data.
CREATE OR REPLACE FUNCTION topology.check_versioning(t text) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    missing         text;
    trigger_count   int;
BEGIN
    SELECT string_agg(e.name || ' ' || e.type, ', ') INTO missing
    FROM (
        SELECT attname::text AS name, format_type(atttypid, atttypmod) AS type
        FROM pg_attribute
        WHERE attrelid = format('topology.%I', t)::regclass AND attnum > 0 AND NOT attisdropped
        UNION ALL
        VALUES ('operation', 'character varying(6)'), ('changed_by', 'uuid'),
               ('actor_type', 'character varying(20)'), ('change_reason', 'character varying(255)')
    ) e
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_attribute h
        WHERE h.attrelid = format('topology.%I', t || '_history')::regclass AND h.attnum > 0 AND NOT h.attisdropped
          AND h.attname = e.name AND format_type(h.atttypid, h.atttypmod) = e.type);

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'topology.%_history is missing or has a different type for: %', t, missing;
    END IF;

    SELECT count(*) INTO trigger_count
    FROM pg_trigger
    WHERE tgrelid = format('topology.%I', t)::regclass AND tgname = 'trg_' || t || '_versioning' AND NOT tgisinternal;

    IF trigger_count <> 1 THEN
        RAISE EXCEPTION 'topology.% is missing its versioning trigger', t;
    END IF;
END $$;

-- Makes topology.<t> system-versioned: creates its history table (or appends columns the main table has gained),
-- writes the versioning and history column comments, and (re)creates the period index, versioning trigger, and
-- <t>_as_of function. Comment the main table's columns before calling it. Idempotent. After any ALTER TABLE or
-- column comment change on a versioned table, call it again in the same migration; a renamed or retyped column must
-- be changed in the history table by hand first. Versions archived before a column was added read NULL for it.
CREATE OR REPLACE FUNCTION topology.enable_versioning(t text) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    main    regclass := format('topology.%I', t)::regclass;
    pk      text;
    cols    text;
    c       record;
BEGIN
    SELECT a.attname INTO pk
    FROM pg_index i
    JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
    WHERE i.indrelid = main AND i.indisprimary;

    SELECT string_agg(format('%I', attname), ', ' ORDER BY attnum) INTO cols
    FROM pg_attribute
    WHERE attrelid = main AND attnum > 0 AND NOT attisdropped;

    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS topology.%I (LIKE topology.%I, '
        'operation VARCHAR(6) NOT NULL, changed_by UUID NULL, actor_type VARCHAR(20) NULL, change_reason VARCHAR(255) NULL)',
        t || '_history', t);
    EXECUTE format('COMMENT ON TABLE topology.%I IS %L', t || '_history',
        'previous versions of topology.' || t || ' (system-versioned; written only by topology.version_row)');

    FOR c IN
        SELECT m.attname, format_type(m.atttypid, m.atttypmod) AS type
        FROM pg_attribute m
        WHERE m.attrelid = main AND m.attnum > 0 AND NOT m.attisdropped
          AND NOT EXISTS (SELECT 1 FROM pg_attribute h
                          WHERE h.attrelid = format('topology.%I', t || '_history')::regclass
                            AND h.attname = m.attname AND NOT h.attisdropped)
        ORDER BY m.attnum
    LOOP
        EXECUTE format('ALTER TABLE topology.%I ADD COLUMN %I %s', t || '_history', c.attname, c.type);
    END LOOP;

    -- Column comments (.ai/rules/sql-ddl.md). The versioning columns get standard text on the main table. Each history
    -- column copies its main column's purpose, with a facts tag for the history column's own type and nullability
    -- (history tables have no keys, defaults, or checks). Columns since dropped from the main table keep their comment.
    EXECUTE format('COMMENT ON COLUMN topology.%I.created_at IS %L', t,
        'When the row was first inserted; never changes. Set by topology.version_row, which overwrites client values. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]');
    EXECUTE format('COMMENT ON COLUMN topology.%I.sys_period IS %L', t,
        'When this version took effect (lower bound, also the row''s last-updated time) until it is replaced (upper bound, open while current). Set by topology.version_row, which overwrites client values. [TSTZRANGE; NOT NULL; DEFAULT open range from clock_timestamp()]');

    FOR c IN
        SELECT h.attname, format_type(h.atttypid, h.atttypmod) AS type, h.attnotnull,
               regexp_replace(col_description(main, m.attnum), '\s*\[[^\[\]]*\]$', '') AS purpose,
               -- what the main column references (its primary key or foreign keys), unenforced in history
               (SELECT string_agg(r.item, '' ORDER BY r.item) FROM (
                    SELECT format('; REF topology.%I(%I), the versioned row', t, m.attname) AS item
                    FROM pg_constraint k WHERE k.conrelid = main AND k.contype = 'p' AND m.attnum = ANY (k.conkey)
                    UNION ALL
                    SELECT format('; REF %I.%I(%s), not enforced in history', rn.nspname, rc.relname,
                                  (SELECT string_agg(format('%I', a.attname), ', ' ORDER BY x.n)
                                   FROM unnest(f.confkey) WITH ORDINALITY AS x(attnum, n)
                                   JOIN pg_attribute a ON a.attrelid = f.confrelid AND a.attnum = x.attnum))
                    FROM pg_constraint f
                    JOIN pg_class rc ON rc.oid = f.confrelid
                    JOIN pg_namespace rn ON rn.oid = rc.relnamespace
                    WHERE f.conrelid = main AND f.contype = 'f' AND m.attnum = ANY (f.conkey)) r) AS refs
        FROM pg_attribute h
        JOIN pg_attribute m ON m.attrelid = main AND m.attname = h.attname AND m.attnum > 0 AND NOT m.attisdropped
        WHERE h.attrelid = format('topology.%I', t || '_history')::regclass AND h.attnum > 0 AND NOT h.attisdropped
    LOOP
        EXECUTE format('COMMENT ON COLUMN topology.%I.%I IS %L', t || '_history', c.attname,
            CASE WHEN c.attname = 'sys_period'
                 THEN 'When this version was current: from when it took effect until the change that replaced or deleted it.'
                 ELSE format('Archived value of topology.%s.%s: %s', t, c.attname, coalesce(c.purpose, '(undocumented)')) END
            || format(' [%s; %s%s]',
                      -- the type as written in DDL, ex. VARCHAR(50), TIMESTAMPTZ, GEOGRAPHY(Point,4326)
                      upper(split_part(replace(replace(c.type, 'character varying', 'varchar'), 'timestamp with time zone', 'timestamptz'), '(', 1))
                          || CASE WHEN c.type LIKE '%(%' THEN '(' || split_part(c.type, '(', 2) ELSE '' END,
                      CASE WHEN c.attnotnull THEN 'NOT NULL' ELSE 'NULL' END,
                      coalesce(c.refs, '')));
    END LOOP;

    EXECUTE format('COMMENT ON COLUMN topology.%I.operation IS %L', t || '_history',
        'Change that ended this version: UPDATE (replaced by a newer version) or DELETE (row removed). [VARCHAR(6); NOT NULL]');
    EXECUTE format('COMMENT ON COLUMN topology.%I.changed_by IS %L', t || '_history',
        'Who made that change, from SET LOCAL rva.actor. NULL = unattributed. [UUID; NULL; REF a user or service id (UUIDv7) owned by the Identity module (Phase 3), no FK]');
    EXECUTE format('COMMENT ON COLUMN topology.%I.actor_type IS %L', t || '_history',
        'Kind of actor that made that change: user, service, or system, from SET LOCAL rva.actor_type. NULL = not given. [VARCHAR(20); NULL]');
    EXECUTE format('COMMENT ON COLUMN topology.%I.change_reason IS %L', t || '_history',
        'Why that change was made, from SET LOCAL rva.change_reason. NULL = not given. [VARCHAR(255); NULL]');

    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON topology.%I USING gist (%I, sys_period)',
        'ix_' || t || '_history_period', t || '_history', pk);

    EXECUTE format(
        'CREATE OR REPLACE TRIGGER %I BEFORE INSERT OR UPDATE OR DELETE ON topology.%I '
        'FOR EACH ROW EXECUTE FUNCTION topology.version_row()',
        'trg_' || t || '_versioning', t);

    -- The table as it was at a point in time: current rows valid then, plus archived versions.
    EXECUTE format(
        'CREATE OR REPLACE FUNCTION topology.%I(point_in_time timestamptz) RETURNS SETOF topology.%I LANGUAGE sql STABLE AS %L',
        t || '_as_of', t,
        format('SELECT %1$s FROM topology.%2$I WHERE sys_period @> point_in_time UNION ALL SELECT %1$s FROM topology.%3$I WHERE sys_period @> point_in_time',
               cols, t, t || '_history'));

    PERFORM topology.check_versioning(t);
END $$;

SELECT topology.enable_versioning(t)
FROM unnest(ARRAY[
    'technology_types', 'protocol_mappings', 'interconnection_types', 'customer_segments', 'grid_node_types',
    'asset_lifecycle_states', 'participation_states', 'grid_states', 'hardware_profiles', 'substations', 'feeders', 'neighborhood_transformers', 'premises', 'microgrids', 'assets',
    'asset_components']) AS t;


-- =====================================================================
-- topology: seed lookups (after versioning, so seeds get created_at/sys_period like any insert)
-- =====================================================================

INSERT INTO topology.technology_types (internal_code, display_name) VALUES
('bess',     'Battery Energy Storage System'),
('solar_pv', 'Solar PV'),
('wind',     'Wind'),
('evse',     'EV Charger (EVSE)')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.interconnection_types (internal_code, display_name) VALUES
('btm', 'Behind-the-Meter'),
('ftm', 'In-Front-of-the-Meter'),
('atm', 'Across-the-Meter')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.customer_segments (internal_code, display_name) VALUES
('residential',           'Residential'),
('commercial_industrial', 'Commercial & Industrial')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.grid_node_types (internal_code, display_name) VALUES
('substation',               'Substation'),
('feeder',                   'Feeder'),
('neighborhood_transformer', 'Neighborhood Transformer'),
('premise',                  'Premise'),
('microgrid',                'Microgrid')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.asset_lifecycle_states (internal_code, display_name) VALUES
('planned',        'Planned'),
('installed',      'Installed'),
('commissioned',   'Commissioned'),
('decommissioned', 'Decommissioned')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.participation_states (internal_code, display_name) VALUES
('not_enrolled', 'Not enrolled'),
('active',       'Active'),
('suspended',    'Suspended'),
('opted_out',    'Opted out')
ON CONFLICT (internal_code) DO NOTHING;

INSERT INTO topology.grid_states (internal_code, display_name, is_grid_connected, is_energized) VALUES
('connected_energized',   'Connected, energized',   TRUE,  TRUE),
('connected_deenergized', 'Connected, de-energized', TRUE,  FALSE),
('islanded',              'Islanded',               FALSE, TRUE),
('disconnected',          'Disconnected',           FALSE, FALSE)
ON CONFLICT (internal_code) DO NOTHING;


-- =====================================================================
-- topology: outbox for integration events (AssetRegistered, AssetUpdated, ...)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.outbox_messages
(
    outbox_message_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id            UUID NOT NULL UNIQUE DEFAULT uuidv7(),
    event_type          VARCHAR(200) NOT NULL,
    payload             JSONB NOT NULL,
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    published_at        TIMESTAMPTZ NULL,
    attempt_count       INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0)
);
COMMENT ON TABLE topology.outbox_messages IS 'integration events written in the same transaction as the change they describe; a background publisher delivers them';
COMMENT ON COLUMN topology.outbox_messages.outbox_message_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN topology.outbox_messages.event_id IS 'Event id sent with the event so consumers can drop duplicates; time-ordered (UUIDv7). [UUID; NOT NULL; DEFAULT uuidv7(); UNIQUE]';
COMMENT ON COLUMN topology.outbox_messages.event_type IS 'Integration event name, ex. AssetUpdated. [VARCHAR(200); NOT NULL]';
COMMENT ON COLUMN topology.outbox_messages.payload IS 'Event body: the integration event contract serialized as JSON. [JSONB; NOT NULL]';
COMMENT ON COLUMN topology.outbox_messages.occurred_at IS 'When the event was written, in the same transaction as the change it describes. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';
COMMENT ON COLUMN topology.outbox_messages.published_at IS 'When the publisher delivered the event. NULL = not yet delivered. [TIMESTAMPTZ; NULL]';
COMMENT ON COLUMN topology.outbox_messages.attempt_count IS 'Delivery attempts so far. [INTEGER; NOT NULL; DEFAULT 0; CHECK >= 0]';
CREATE INDEX IF NOT EXISTS ix_topology_outbox_unpublished ON topology.outbox_messages (occurred_at) WHERE published_at IS NULL;


-- =====================================================================
-- simulation: lookups
-- =====================================================================

CREATE TABLE IF NOT EXISTS simulation.simulation_run_status_types
(
    status_type_id      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                VARCHAR(50) NOT NULL UNIQUE,
    description         VARCHAR(255)
);
COMMENT ON TABLE simulation.simulation_run_status_types IS 'lifecycle states of a simulation run: Pending, Running, Completed, Failed';
COMMENT ON COLUMN simulation.simulation_run_status_types.status_type_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.simulation_run_status_types.name IS 'Status name, also used as its machine code; simulation.require_pending_run looks up Pending by it. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN simulation.simulation_run_status_types.description IS 'What the status means. NULL = none. [VARCHAR(255); NULL]';

INSERT INTO simulation.simulation_run_status_types (name, description) VALUES
('Pending',   'Initialized but waiting for resources or worker allocation.'),
('Running',   'Actively ticking and streaming telemetry.'),
('Completed', 'Finished successfully within its allocated time window.'),
('Failed',    'Aborted due to an unhandled exception, configuration error, or worker crash.')
ON CONFLICT (name) DO NOTHING;

CREATE TABLE IF NOT EXISTS simulation.scope_levels
(
    scope_level_id      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL
);
COMMENT ON TABLE simulation.scope_levels IS 'grid level a run''s scope starts from; the root itself is identified by code and resolved through GridTopology''s contract';
COMMENT ON COLUMN simulation.scope_levels.scope_level_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.scope_levels.internal_code IS 'Stable machine code, matching GridTopology''s node levels: substation, feeder, neighborhood_transformer, premise. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN simulation.scope_levels.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

INSERT INTO simulation.scope_levels (internal_code, display_name) VALUES
('substation',               'Substation'),
('feeder',                   'Feeder'),
('neighborhood_transformer', 'Neighborhood Transformer'),
('premise',                  'Premise'),
('microgrid',                'Microgrid')
ON CONFLICT (internal_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS simulation.detail_levels
(
    detail_level_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL
);
COMMENT ON TABLE simulation.detail_levels IS 'how much of the scoped topology a run loads';
COMMENT ON COLUMN simulation.detail_levels.detail_level_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.detail_levels.internal_code IS 'Stable machine code: hierarchy, assets, assets_with_components. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN simulation.detail_levels.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

INSERT INTO simulation.detail_levels (internal_code, display_name) VALUES
('hierarchy',              'Grid hierarchy only'),
('assets',                 'Hierarchy and assets'),
('assets_with_components', 'Hierarchy, assets, and asset components')
ON CONFLICT (internal_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS simulation.run_kinds
(
    run_kind_id         INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL
);
COMMENT ON TABLE simulation.run_kinds IS 'what a run is for. Only chaos exists today; digital-twin kinds (forecast from live state, what-if, replay) are added as rows';
COMMENT ON COLUMN simulation.run_kinds.run_kind_id IS 'Surrogate key. [INTEGER; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.run_kinds.internal_code IS 'Stable machine code, ex. chaos. [VARCHAR(50); NOT NULL; UNIQUE]';
COMMENT ON COLUMN simulation.run_kinds.display_name IS 'Human-readable name for UIs and reports. [VARCHAR(100); NOT NULL]';

INSERT INTO simulation.run_kinds (internal_code, display_name) VALUES
('chaos', 'Chaos Engine fault-injection scenario')
ON CONFLICT (internal_code) DO NOTHING;


-- =====================================================================
-- simulation: run ledger (append-only events) and per-run asset overrides
-- =====================================================================

CREATE TABLE IF NOT EXISTS simulation.simulation_runs
(
    simulation_run_id   UUID PRIMARY KEY DEFAULT uuidv7(),  -- time-ordered UUID (PostgreSQL 18+)
    scenario_name       VARCHAR(100) NOT NULL,
    run_kind_id         INTEGER NOT NULL REFERENCES simulation.run_kinds (run_kind_id),
    status_type_id      INTEGER NOT NULL REFERENCES simulation.simulation_run_status_types (status_type_id),
    -- scope: which part of the grid, how much detail, and which version of it
    scope_level_id      INTEGER NOT NULL REFERENCES simulation.scope_levels (scope_level_id),
    scope_root_code     VARCHAR(50) NOT NULL,
    detail_level_id     INTEGER NOT NULL REFERENCES simulation.detail_levels (detail_level_id),
    topology_as_of      TIMESTAMPTZ NOT NULL,
    simulated_window    TSTZRANGE NULL,
    initial_state_as_of TIMESTAMPTZ NULL,
    configuration_json  JSONB,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    started_at          TIMESTAMPTZ NULL,
    ended_at            TIMESTAMPTZ NULL,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_simulation_runs_started CHECK (started_at IS NULL OR started_at >= created_at),
    CONSTRAINT ck_simulation_runs_ended   CHECK (ended_at IS NULL OR (ended_at >= created_at AND (started_at IS NULL OR ended_at >= started_at))),
    CONSTRAINT ck_simulation_runs_topology_as_of CHECK (topology_as_of <= created_at),
    CONSTRAINT ck_simulation_runs_initial_state_as_of CHECK (initial_state_as_of IS NULL OR initial_state_as_of <= created_at),
    CONSTRAINT ck_simulation_runs_window  CHECK (NOT isempty(simulated_window) AND NOT lower_inf(simulated_window))
);
COMMENT ON TABLE simulation.simulation_runs IS 'one simulation run: what it is for, which part of the grid it covers and in how much detail, which topology version and starting state it uses, and its current status. Scope and kind are fixed at creation; window, configuration, and overrides only while Pending. Status moves Pending -> Running -> Completed, or to Failed from Pending or Running (simulation.check_run_lifecycle). Status history is in simulation_run_events; runs are never deleted';
COMMENT ON COLUMN simulation.simulation_runs.simulation_run_id IS 'Run id, time-ordered (UUIDv7). [UUID; NOT NULL; DEFAULT uuidv7(); PK]';
COMMENT ON COLUMN simulation.simulation_runs.scenario_name IS 'Human-readable scenario name. [VARCHAR(100); NOT NULL]';
COMMENT ON COLUMN simulation.simulation_runs.run_kind_id IS 'What the run is for, ex. chaos. Fixed at creation by trigger. [INTEGER; NOT NULL; FK simulation.run_kinds(run_kind_id)]';
COMMENT ON COLUMN simulation.simulation_runs.status_type_id IS 'Current status. A run is created Pending and moves Pending -> Running -> Completed, or to Failed from Pending or Running; entering Failed needs SET LOCAL rva.change_reason (simulation.check_run_lifecycle). Every change is recorded in simulation_run_events by trigger, with the actor and reason from SET LOCAL rva.*. [INTEGER; NOT NULL; FK simulation.simulation_run_status_types(status_type_id)]';
COMMENT ON COLUMN simulation.simulation_runs.scope_level_id IS 'Grid level the run''s scope starts from. Fixed at creation by trigger. [INTEGER; NOT NULL; FK simulation.scope_levels(scope_level_id)]';
COMMENT ON COLUMN simulation.simulation_runs.scope_root_code IS 'Code of the grid node the scope starts from, at scope_level_id, ex. PDX-SUB-ARBOR or a microgrid_code. Fixed at creation by trigger. [VARCHAR(50); NOT NULL; REF a GridTopology node code (substation_code, feeder_code, ...), validated through IGridTopologyQueries, no cross-module FK]';
COMMENT ON COLUMN simulation.simulation_runs.detail_level_id IS 'How much of the scoped topology the run loads. Fixed at creation by trigger. [INTEGER; NOT NULL; FK simulation.detail_levels(detail_level_id)]';
COMMENT ON COLUMN simulation.simulation_runs.topology_as_of IS 'Topology version the run uses (read through the topology _as_of functions), so the run can be reproduced. Set to created_at by trigger when not given; fixed at creation. [TIMESTAMPTZ; NOT NULL; CHECK <= created_at]';
COMMENT ON COLUMN simulation.simulation_runs.simulated_window IS 'Simulated time the run covers, ex. a replayed January storm; independent of wall-clock execution. Must be non-empty with a start; the end may be open. Can change only while Pending. NULL = not tied to a calendar period. [TSTZRANGE; NULL; CHECK not empty and has a lower bound]';
COMMENT ON COLUMN simulation.simulation_runs.initial_state_as_of IS 'Start from measured asset state at this moment. NULL = synthetic start (asset defaults plus overrides). Fixed at creation. [TIMESTAMPTZ; NULL; CHECK <= created_at]';
COMMENT ON COLUMN simulation.simulation_runs.configuration_json IS 'Scenario rules (faults, switching, load steps); refers to assets by asset_code. Can change only while Pending. NULL = no rules. [JSONB; NULL]';
COMMENT ON COLUMN simulation.simulation_runs.created_at IS 'When the run was created. Set by trigger and never changes; client values are overwritten. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp(); CHECK not after started_at, ended_at, and no earlier than topology_as_of and initial_state_as_of]';
COMMENT ON COLUMN simulation.simulation_runs.started_at IS 'When the run started. NULL while Pending; set once Running (simulation.check_run_lifecycle). [TIMESTAMPTZ; NULL; CHECK >= created_at]';
COMMENT ON COLUMN simulation.simulation_runs.ended_at IS 'When the run Completed or Failed; a run can fail before it starts. NULL while Pending or Running, set once ended (simulation.check_run_lifecycle). [TIMESTAMPTZ; NULL; CHECK >= created_at and >= started_at]';
COMMENT ON COLUMN simulation.simulation_runs.updated_at IS 'When the row last changed. Set by trigger. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';
CREATE INDEX IF NOT EXISTS ix_simulation_runs_status_type_id ON simulation.simulation_runs (status_type_id);
CREATE INDEX IF NOT EXISTS ix_simulation_runs_run_kind_id ON simulation.simulation_runs (run_kind_id);

CREATE TABLE IF NOT EXISTS simulation.simulation_run_events
(
    simulation_run_event_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    simulation_run_id        UUID NOT NULL REFERENCES simulation.simulation_runs (simulation_run_id),
    status_type_id           INTEGER NOT NULL REFERENCES simulation.simulation_run_status_types (status_type_id),
    occurred_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    reason                   VARCHAR(500) NULL,
    actor_id                 UUID NULL,
    actor_type               VARCHAR(20) NULL,
    details                  JSONB NULL
);
COMMENT ON TABLE simulation.simulation_run_events IS 'append-only lifecycle history of each run, written by trigger on every status change. Runs with events cannot be deleted';
COMMENT ON COLUMN simulation.simulation_run_events.simulation_run_event_id IS 'Surrogate key; increases with insertion order. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.simulation_run_events.simulation_run_id IS 'Run whose status changed. [UUID; NOT NULL; FK simulation.simulation_runs(simulation_run_id)]';
COMMENT ON COLUMN simulation.simulation_run_events.status_type_id IS 'Status the run entered. [INTEGER; NOT NULL; FK simulation.simulation_run_status_types(status_type_id)]';
COMMENT ON COLUMN simulation.simulation_run_events.occurred_at IS 'When the run entered the status. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';
COMMENT ON COLUMN simulation.simulation_run_events.reason IS 'Why the status changed, ex. a failure cause; from SET LOCAL rva.change_reason. NULL = not given. [VARCHAR(500); NULL]';
COMMENT ON COLUMN simulation.simulation_run_events.actor_id IS 'Who changed the status; from SET LOCAL rva.actor. NULL = unattributed. [UUID; NULL; REF a user or service id (UUIDv7) owned by the Identity module (Phase 3), no FK]';
COMMENT ON COLUMN simulation.simulation_run_events.actor_type IS 'Kind of actor: user, service, or system; from SET LOCAL rva.actor_type. NULL = not given. [VARCHAR(20); NULL]';
COMMENT ON COLUMN simulation.simulation_run_events.details IS 'Additional event data as JSON. NULL = none. [JSONB; NULL]';
CREATE INDEX IF NOT EXISTS ix_simulation_run_events_run ON simulation.simulation_run_events (simulation_run_id, occurred_at);

CREATE TABLE IF NOT EXISTS simulation.simulation_asset_states
(
    simulation_asset_state_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    simulation_run_id           UUID NOT NULL REFERENCES simulation.simulation_runs (simulation_run_id),
    asset_id                    BIGINT NOT NULL,
    is_available                BOOLEAN NOT NULL DEFAULT TRUE,
    p_min_kw                    DOUBLE PRECISION NULL,
    p_max_kw                    DOUBLE PRECISION NULL,
    state_of_charge_pct         DOUBLE PRECISION NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uk_simulation_asset UNIQUE (simulation_run_id, asset_id),
    CONSTRAINT ck_simulation_asset_states_power_bounds CHECK (p_min_kw IS NULL OR p_max_kw IS NULL OR p_min_kw <= p_max_kw),
    CONSTRAINT ck_simulation_asset_states_soc CHECK (state_of_charge_pct IS NULL OR state_of_charge_pct BETWEEN 0 AND 100)
);
COMMENT ON TABLE simulation.simulation_asset_states IS 'per-run overrides of each asset''s starting state (run inputs), at most one per asset. Can only be changed while the run is Pending. Changes during a run (faults, switching, load steps) belong in configuration_json or a future per-run event table, not here';
COMMENT ON COLUMN simulation.simulation_asset_states.simulation_asset_state_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.simulation_asset_states.simulation_run_id IS 'Run the override belongs to; it must be Pending for the override to change. [UUID; NOT NULL; FK simulation.simulation_runs(simulation_run_id); UNIQUE (simulation_run_id, asset_id)]';
COMMENT ON COLUMN simulation.simulation_asset_states.asset_id IS 'Asset whose starting state is overridden. [BIGINT; NOT NULL; UNIQUE (simulation_run_id, asset_id); REF topology.assets(asset_id), validated through IGridTopologyQueries, no cross-module FK]';
COMMENT ON COLUMN simulation.simulation_asset_states.is_available IS 'FALSE = the asset starts the run out of service. [BOOLEAN; NOT NULL; DEFAULT TRUE]';
COMMENT ON COLUMN simulation.simulation_asset_states.p_min_kw IS 'Override of the asset''s minimum real power in kW; negative = charging or import. NULL = use the asset''s own rating. [DOUBLE PRECISION; NULL; CHECK <= p_max_kw when both are set]';
COMMENT ON COLUMN simulation.simulation_asset_states.p_max_kw IS 'Override of the asset''s maximum real power in kW. NULL = use the asset''s own rating. [DOUBLE PRECISION; NULL; CHECK >= p_min_kw when both are set]';
COMMENT ON COLUMN simulation.simulation_asset_states.state_of_charge_pct IS 'Starting state of charge in percent, storage only. NULL = the run''s default. [DOUBLE PRECISION; NULL; CHECK between 0 and 100]';
COMMENT ON COLUMN simulation.simulation_asset_states.created_at IS 'When the override was added. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';

-- Run defaults and bookkeeping: topology_as_of defaults to created_at; updated_at tracks every change.
-- What a run is, what it covers, and what it starts from (kind, scope, topology_as_of, initial_state_as_of) are fixed at
-- creation; the simulated window and configuration are fixed once the run leaves Pending. Changes are ignored, as for
-- created_at, so a run can be reproduced exactly.
CREATE OR REPLACE FUNCTION simulation.simulation_runs_bookkeeping() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.created_at     := clock_timestamp();
        NEW.topology_as_of := COALESCE(NEW.topology_as_of, NEW.created_at);
        NEW.updated_at     := NEW.created_at;
    ELSE
        NEW.created_at          := OLD.created_at;      -- immutable
        NEW.run_kind_id         := OLD.run_kind_id;     -- a run's inputs are fixed at creation
        NEW.topology_as_of      := OLD.topology_as_of;
        NEW.initial_state_as_of := OLD.initial_state_as_of;
        NEW.scope_level_id      := OLD.scope_level_id;
        NEW.scope_root_code     := OLD.scope_root_code;
        NEW.detail_level_id     := OLD.detail_level_id;
        IF OLD.status_type_id IS DISTINCT FROM (SELECT status_type_id FROM simulation.simulation_run_status_types WHERE name = 'Pending') THEN
            NEW.simulated_window   := OLD.simulated_window;
            NEW.configuration_json := OLD.configuration_json;
        END IF;
        NEW.updated_at          := clock_timestamp();
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_runs_bookkeeping
    BEFORE INSERT OR UPDATE ON simulation.simulation_runs
    FOR EACH ROW EXECUTE FUNCTION simulation.simulation_runs_bookkeeping();

-- Lifecycle rules, also enforced by the SimulationRun entity: a run is created Pending; it moves Pending -> Running ->
-- Completed, or to Failed from Pending or Running; Completed and Failed are terminal. started_at is set exactly once the
-- run has started (Running, Completed, or Failed after starting) and ended_at exactly when it has ended. Entering Failed
-- needs SET LOCAL rva.change_reason, so every failure is recorded with its cause. Fires after
-- trg_simulation_runs_bookkeeping (triggers fire in name order).
CREATE OR REPLACE FUNCTION simulation.check_run_lifecycle() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    old_status  text;
    new_status  text;
BEGIN
    SELECT name INTO new_status FROM simulation.simulation_run_status_types WHERE status_type_id = NEW.status_type_id;

    IF TG_OP = 'INSERT' THEN
        IF new_status <> 'Pending' THEN
            RAISE EXCEPTION 'a run is created Pending, not %', new_status USING ERRCODE = 'check_violation';
        END IF;
    ELSE
        SELECT name INTO old_status FROM simulation.simulation_run_status_types WHERE status_type_id = OLD.status_type_id;
        IF new_status <> old_status AND (old_status, new_status) NOT IN
               (('Pending', 'Running'), ('Pending', 'Failed'), ('Running', 'Completed'), ('Running', 'Failed')) THEN
            RAISE EXCEPTION 'run %: % -> % is not a valid transition', NEW.simulation_run_id, old_status, new_status USING ERRCODE = 'check_violation';
        END IF;
        IF new_status = 'Failed' AND old_status <> 'Failed' AND coalesce(current_setting('rva.change_reason', true), '') = '' THEN
            RAISE EXCEPTION 'run % cannot enter Failed without SET LOCAL rva.change_reason', NEW.simulation_run_id USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    IF (NEW.started_at IS NOT NULL) <> (new_status IN ('Running', 'Completed') OR (new_status = 'Failed' AND NEW.started_at IS NOT NULL)) THEN
        RAISE EXCEPTION 'run %: started_at is set exactly once the run has started (status %)', NEW.simulation_run_id, new_status USING ERRCODE = 'check_violation';
    END IF;
    IF (NEW.ended_at IS NOT NULL) <> (new_status IN ('Completed', 'Failed')) THEN
        RAISE EXCEPTION 'run %: ended_at is set exactly when the run has ended (status %)', NEW.simulation_run_id, new_status USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_runs_lifecycle
    BEFORE INSERT OR UPDATE ON simulation.simulation_runs
    FOR EACH ROW EXECUTE FUNCTION simulation.check_run_lifecycle();

-- Every status a run enters is recorded as an event, so the history cannot be skipped by application code.
CREATE OR REPLACE FUNCTION simulation.record_run_event() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' OR NEW.status_type_id IS DISTINCT FROM OLD.status_type_id THEN
        INSERT INTO simulation.simulation_run_events (simulation_run_id, status_type_id, reason, actor_id, actor_type)
        VALUES (NEW.simulation_run_id, NEW.status_type_id,
                NULLIF(current_setting('rva.change_reason', true), ''),
                NULLIF(current_setting('rva.actor', true), '')::uuid,
                NULLIF(current_setting('rva.actor_type', true), ''));
    END IF;
    RETURN NULL;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_runs_record_event
    AFTER INSERT OR UPDATE OF status_type_id ON simulation.simulation_runs
    FOR EACH ROW EXECUTE FUNCTION simulation.record_run_event();

-- Append-only enforcement for event tables.
CREATE OR REPLACE FUNCTION simulation.reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '%.% is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_run_events_append_only
    BEFORE UPDATE OR DELETE ON simulation.simulation_run_events
    FOR EACH ROW EXECUTE FUNCTION simulation.reject_change();

-- Run inputs are fixed once a run leaves Pending, so a run can be reproduced exactly.
CREATE OR REPLACE FUNCTION simulation.require_pending_run() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    run_id      uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.simulation_run_id ELSE NEW.simulation_run_id END;
    run_status  text;
BEGIN
    SELECT s.name INTO run_status
    FROM simulation.simulation_runs r
    JOIN simulation.simulation_run_status_types s ON s.status_type_id = r.status_type_id
    WHERE r.simulation_run_id = run_id;

    IF run_status IS DISTINCT FROM 'Pending' THEN
        RAISE EXCEPTION 'overrides for run % cannot change: the run is %, not Pending', run_id, run_status;
    END IF;

    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_asset_states_pending_only
    BEFORE INSERT OR UPDATE OR DELETE ON simulation.simulation_asset_states
    FOR EACH ROW EXECUTE FUNCTION simulation.require_pending_run();


-- =====================================================================
-- simulation: outbox for integration events (SimulationRunStarted, SimulationRunEnded)
-- =====================================================================

CREATE TABLE IF NOT EXISTS simulation.outbox_messages
(
    outbox_message_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id            UUID NOT NULL UNIQUE DEFAULT uuidv7(),
    event_type          VARCHAR(200) NOT NULL,
    payload             JSONB NOT NULL,
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    published_at        TIMESTAMPTZ NULL,
    attempt_count       INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0)
);
COMMENT ON TABLE simulation.outbox_messages IS 'integration events written in the same transaction as the change they describe; a background publisher delivers them';
COMMENT ON COLUMN simulation.outbox_messages.outbox_message_id IS 'Surrogate key. [BIGINT; NOT NULL; IDENTITY; PK]';
COMMENT ON COLUMN simulation.outbox_messages.event_id IS 'Event id sent with the event so consumers can drop duplicates; time-ordered (UUIDv7). [UUID; NOT NULL; DEFAULT uuidv7(); UNIQUE]';
COMMENT ON COLUMN simulation.outbox_messages.event_type IS 'Integration event name, ex. SimulationRunStarted. [VARCHAR(200); NOT NULL]';
COMMENT ON COLUMN simulation.outbox_messages.payload IS 'Event body: the integration event contract serialized as JSON. [JSONB; NOT NULL]';
COMMENT ON COLUMN simulation.outbox_messages.occurred_at IS 'When the event was written, in the same transaction as the change it describes. [TIMESTAMPTZ; NOT NULL; DEFAULT clock_timestamp()]';
COMMENT ON COLUMN simulation.outbox_messages.published_at IS 'When the publisher delivered the event. NULL = not yet delivered. [TIMESTAMPTZ; NULL]';
COMMENT ON COLUMN simulation.outbox_messages.attempt_count IS 'Delivery attempts so far. [INTEGER; NOT NULL; DEFAULT 0; CHECK >= 0]';
CREATE INDEX IF NOT EXISTS ix_simulation_outbox_unpublished ON simulation.outbox_messages (occurred_at) WHERE published_at IS NULL;


-- =====================================================================
-- self-check: every versioned topology table matches its history table and has its trigger, every grid node table
-- has its identity trigger, and every table and column has a comment. Fails initialization if a later schema change
-- forgets any of them. The comments' content is checked by infrastructure/postgres/tests/ddl_lint.sql.
-- =====================================================================

DO $$
DECLARE
    t               text;
    undocumented    text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'technology_types', 'protocol_mappings', 'interconnection_types', 'customer_segments', 'grid_node_types',
        'asset_lifecycle_states', 'participation_states', 'grid_states', 'hardware_profiles', 'substations', 'feeders', 'neighborhood_transformers', 'premises', 'microgrids', 'assets',
        'asset_components']
    LOOP
        PERFORM topology.check_versioning(t);
    END LOOP;

    FOREACH t IN ARRAY ARRAY['substations', 'feeders', 'neighborhood_transformers', 'premises', 'microgrids']
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_trigger
                       WHERE tgrelid = format('topology.%I', t)::regclass AND tgname = 'trg_' || t || '_grid_node') THEN
            RAISE EXCEPTION 'topology.% is missing its grid node trigger', t;
        END IF;
    END LOOP;

    SELECT string_agg(o.name, ', ' ORDER BY o.name) INTO undocumented
    FROM (
        SELECT format('%I.%I', n.nspname, c.relname) AS name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('topology', 'simulation') AND c.relkind IN ('r', 'p')
          AND coalesce(btrim(obj_description(c.oid, 'pg_class')), '') = ''
        UNION ALL
        SELECT format('%I.%I.%I', n.nspname, c.relname, a.attname)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        WHERE n.nspname IN ('topology', 'simulation') AND c.relkind IN ('r', 'p')
          AND coalesce(btrim(col_description(c.oid, a.attnum)), '') = ''
    ) o;
    IF undocumented IS NOT NULL THEN
        RAISE EXCEPTION 'missing comments (see .ai/rules/sql-ddl.md): %', undocumented;
    END IF;
END $$;
