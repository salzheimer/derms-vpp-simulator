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

Change tracking (csharp-monolith.md section 9):
  topology   - state that gets edited is SYSTEM-VERSIONED: every table has created_at and sys_period, a
               <table>_history twin, a versioning trigger, and a <table>_as_of(timestamptz) function.
               lower(sys_period) is when the current version took effect (it doubles as updated_at).
               Who/why come from per-transaction settings set by the module:
                 SET LOCAL rva.actor = '<uuid>'; SET LOCAL rva.actor_type = 'user|service|system';
                 SET LOCAL rva.change_reason = '...';
  simulation - things that happen are APPEND-ONLY EVENTS: run lifecycle transitions are recorded in
               simulation_run_events by trigger, and run inputs cannot change once a run leaves Pending.
  both       - an outbox_messages table per module for reliable integration events.
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
COMMENT ON COLUMN topology.technology_types.internal_code IS 'unique internal code for identifying the tech type';

CREATE TABLE IF NOT EXISTS topology.protocol_mappings
(
    protocol_mapping_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    technology_type_id      INTEGER NOT NULL REFERENCES topology.technology_types (technology_type_id) ON DELETE CASCADE,
    protocol_standard       VARCHAR(50) NOT NULL,   -- e.g., 'OpenADR_3.0', 'IEEE_2030_5'
    resource_category       VARCHAR(100) NOT NULL,  -- e.g., 'Storage'
    asset_type_code         VARCHAR(100) NOT NULL,  -- e.g., 'Battery'
    extra_attributes        JSONB NULL,             -- For edge-case capabilities
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
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
    display_name                VARCHAR(100) NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                  TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.interconnection_types IS 'meter position of an asset relative to the customer meter. Only ambiguous for premise-connected assets; feeder/substation-connected assets are always ftm (enforced in the Asset entity)';

CREATE TABLE IF NOT EXISTS topology.customer_segments
(
    customer_segment_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code           VARCHAR(50) NOT NULL UNIQUE,
    display_name            VARCHAR(100) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
COMMENT ON TABLE topology.customer_segments IS 'the utility service class of a premise, ex. Residential. Independent of equipment size';


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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
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
    location                GEOGRAPHY(Point, 4326),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);

CREATE TABLE IF NOT EXISTS topology.feeders
(
    feeder_id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    substation_id           BIGINT NOT NULL REFERENCES topology.substations (substation_id),
    feeder_code             VARCHAR(50) NOT NULL UNIQUE,
    nominal_voltage_kv      DOUBLE PRECISION NOT NULL CHECK (nominal_voltage_kv > 0),   -- e.g. 12.47
    rated_capacity_kva      DOUBLE PRECISION NULL CHECK (rated_capacity_kva > 0),
    line_geometry           GEOMETRY(LineString, 4326),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
);
CREATE INDEX IF NOT EXISTS ix_feeders_substation_id ON topology.feeders (substation_id);

CREATE TABLE IF NOT EXISTS topology.neighborhood_transformers
(
    neighborhood_transformer_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    feeder_id                       BIGINT NOT NULL REFERENCES topology.feeders (feeder_id),
    neighborhood_transformer_code   VARCHAR(50) NOT NULL UNIQUE,
    rated_capacity_kva              DOUBLE PRECISION NOT NULL CHECK (rated_capacity_kva > 0),   -- e.g. 50
    secondary_voltage_v             DOUBLE PRECISION NOT NULL CHECK (secondary_voltage_v > 0),  -- e.g. 240
    location                        GEOGRAPHY(Point, 4326),
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
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
    location                        GEOGRAPHY(Point, 4326),
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)')
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
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
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period                      TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    sys_period              TSTZRANGE NOT NULL DEFAULT tstzrange(clock_timestamp(), NULL, '[)'),
    -- a component's profile must be an energy product of the same technology (rejects control-only equipment)
    CONSTRAINT fk_asset_components_hardware_profile FOREIGN KEY (hardware_profile_id, technology_type_id)
        REFERENCES topology.hardware_profiles (hardware_profile_id, technology_type_id)
);
COMMENT ON TABLE topology.asset_components IS 'what an asset is made of, for simulation. Components are never dispatched directly';
CREATE INDEX IF NOT EXISTS ix_asset_components_asset_id ON topology.asset_components (asset_id);


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
    EXECUTE format('INSERT INTO %I.%I SELECT ($1).*, $2, $3, $4, $5', TG_TABLE_SCHEMA, TG_TABLE_NAME || '_history')
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

-- For each versioned table: create its history twin, period index, versioning trigger, and as_of function.
-- History columns = the main table's columns (same order) + operation, changed_by, actor_type, change_reason.
DO $$
DECLARE
    t       text;
    pk      text;
    cols    text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'technology_types', 'protocol_mappings', 'interconnection_types', 'customer_segments', 'hardware_profiles',
        'substations', 'feeders', 'neighborhood_transformers', 'premises', 'microgrids', 'assets', 'asset_components']
    LOOP
        SELECT a.attname INTO pk
        FROM pg_index i
        JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
        WHERE i.indrelid = format('topology.%I', t)::regclass AND i.indisprimary;

        SELECT string_agg(format('%I', attname), ', ' ORDER BY attnum) INTO cols
        FROM pg_attribute
        WHERE attrelid = format('topology.%I', t)::regclass AND attnum > 0 AND NOT attisdropped;

        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS topology.%I (LIKE topology.%I, '
            'operation VARCHAR(6) NOT NULL, changed_by UUID NULL, actor_type VARCHAR(20) NULL, change_reason VARCHAR(255) NULL)',
            t || '_history', t);
        EXECUTE format('COMMENT ON TABLE topology.%I IS %L', t || '_history',
            'previous versions of topology.' || t || ' (system-versioned; written only by topology.version_row)');

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
            format('SELECT * FROM topology.%I WHERE sys_period @> point_in_time UNION ALL SELECT %s FROM topology.%I WHERE sys_period @> point_in_time',
                   t, cols, t || '_history'));
    END LOOP;
END $$;


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


-- =====================================================================
-- topology: outbox for integration events (AssetRegistered, AssetUpdated, ...)
-- =====================================================================

CREATE TABLE IF NOT EXISTS topology.outbox_messages
(
    outbox_message_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id            UUID NOT NULL UNIQUE DEFAULT uuidv7(),
    event_type          VARCHAR(200) NOT NULL,   -- e.g. 'AssetUpdated'
    payload             JSONB NOT NULL,
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    published_at        TIMESTAMPTZ NULL,
    attempt_count       INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0)
);
COMMENT ON TABLE topology.outbox_messages IS 'integration events written in the same transaction as the change they describe; a background publisher delivers them';
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

INSERT INTO simulation.scope_levels (internal_code, display_name) VALUES
('substation',               'Substation'),
('feeder',                   'Feeder'),
('neighborhood_transformer', 'Neighborhood Transformer'),
('premise',                  'Premise')
ON CONFLICT (internal_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS simulation.detail_levels
(
    detail_level_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    internal_code       VARCHAR(50) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL
);
COMMENT ON TABLE simulation.detail_levels IS 'how much of the scoped topology a run loads';

INSERT INTO simulation.detail_levels (internal_code, display_name) VALUES
('hierarchy',              'Grid hierarchy only'),
('assets',                 'Hierarchy and assets'),
('assets_with_components', 'Hierarchy, assets, and asset components')
ON CONFLICT (internal_code) DO NOTHING;


-- =====================================================================
-- simulation: run ledger (append-only events) and per-run asset overrides
-- =====================================================================

CREATE TABLE IF NOT EXISTS simulation.simulation_runs
(
    simulation_run_id   UUID PRIMARY KEY DEFAULT uuidv7(),  -- time-ordered UUID (PostgreSQL 18+)
    scenario_name       VARCHAR(100) NOT NULL,
    status_type_id      INTEGER NOT NULL REFERENCES simulation.simulation_run_status_types (status_type_id),  -- current status; history in simulation_run_events
    -- scope: which part of the grid, how much detail, and which version of it
    scope_level_id      INTEGER NOT NULL REFERENCES simulation.scope_levels (scope_level_id),
    scope_root_code     VARCHAR(50) NOT NULL,   -- e.g. 'PDX-SUB-ARBOR'; validated through IGridTopologyQueries
    detail_level_id     INTEGER NOT NULL REFERENCES simulation.detail_levels (detail_level_id),
    topology_as_of      TIMESTAMPTZ NOT NULL,   -- reproducibility: the topology version this run uses; defaults to created_at
    simulated_window    TSTZRANGE NULL,         -- simulated time, e.g. a replayed January storm (independent of wall-clock execution)
    configuration_json  JSONB,                  -- scenario rules; refers to assets by asset_code
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    started_at          TIMESTAMPTZ NULL,       -- NULL while Pending
    ended_at            TIMESTAMPTZ NULL,       -- set on Completed or Failed (a run can fail before it starts)
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_simulation_runs_started CHECK (started_at IS NULL OR started_at >= created_at),
    CONSTRAINT ck_simulation_runs_ended   CHECK (ended_at IS NULL OR (ended_at >= created_at AND (started_at IS NULL OR ended_at >= started_at))),
    CONSTRAINT ck_simulation_runs_topology_as_of CHECK (topology_as_of <= created_at)
);
CREATE INDEX IF NOT EXISTS ix_simulation_runs_status_type_id ON simulation.simulation_runs (status_type_id);

CREATE TABLE IF NOT EXISTS simulation.simulation_run_events
(
    simulation_run_event_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    simulation_run_id        UUID NOT NULL REFERENCES simulation.simulation_runs (simulation_run_id),
    status_type_id           INTEGER NOT NULL REFERENCES simulation.simulation_run_status_types (status_type_id),
    occurred_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    reason                   VARCHAR(500) NULL,   -- e.g. failure cause, from SET LOCAL rva.change_reason
    actor_id                 UUID NULL,           -- from SET LOCAL rva.actor
    actor_type               VARCHAR(20) NULL,    -- user | service | system, from SET LOCAL rva.actor_type
    details                  JSONB NULL
);
COMMENT ON TABLE simulation.simulation_run_events IS 'append-only lifecycle history of each run, written by trigger on every status change. Runs with events cannot be deleted';
CREATE INDEX IF NOT EXISTS ix_simulation_run_events_run ON simulation.simulation_run_events (simulation_run_id, occurred_at);

CREATE TABLE IF NOT EXISTS simulation.simulation_asset_states
(
    simulation_asset_state_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    simulation_run_id           UUID NOT NULL REFERENCES simulation.simulation_runs (simulation_run_id),
    asset_id                    BIGINT NOT NULL,  -- owned by GridTopology; validated via IGridTopologyQueries, no FK by design
    is_available                BOOLEAN NOT NULL DEFAULT TRUE,
    p_min_kw                    DOUBLE PRECISION NULL,  -- NULL = no override; use the asset's own bound
    p_max_kw                    DOUBLE PRECISION NULL,
    state_of_charge_pct         DOUBLE PRECISION NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uk_simulation_asset UNIQUE (simulation_run_id, asset_id),
    CONSTRAINT ck_simulation_asset_states_power_bounds CHECK (p_min_kw IS NULL OR p_max_kw IS NULL OR p_min_kw <= p_max_kw),
    CONSTRAINT ck_simulation_asset_states_soc CHECK (state_of_charge_pct IS NULL OR state_of_charge_pct BETWEEN 0 AND 100)
);
COMMENT ON TABLE simulation.simulation_asset_states IS 'per-run asset overrides (run inputs). Can only be changed while the run is Pending';

-- Run defaults and bookkeeping: topology_as_of defaults to created_at; updated_at tracks every change.
CREATE OR REPLACE FUNCTION simulation.simulation_runs_bookkeeping() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.created_at     := clock_timestamp();
        NEW.topology_as_of := COALESCE(NEW.topology_as_of, NEW.created_at);
        NEW.updated_at     := NEW.created_at;
    ELSE
        NEW.created_at     := OLD.created_at;      -- immutable
        NEW.topology_as_of := OLD.topology_as_of;  -- a run's inputs are fixed at creation
        NEW.updated_at     := clock_timestamp();
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_simulation_runs_bookkeeping
    BEFORE INSERT OR UPDATE ON simulation.simulation_runs
    FOR EACH ROW EXECUTE FUNCTION simulation.simulation_runs_bookkeeping();

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
CREATE INDEX IF NOT EXISTS ix_simulation_outbox_unpublished ON simulation.outbox_messages (occurred_at) WHERE published_at IS NULL;


-- =====================================================================
-- self-check: every versioned topology table has a matching history twin and trigger.
-- Fails initialization if a later schema change forgets to update the history table.
-- =====================================================================

DO $$
DECLARE
    t               text;
    expected        text;
    actual          text;
    trigger_count   int;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'technology_types', 'protocol_mappings', 'interconnection_types', 'customer_segments', 'hardware_profiles',
        'substations', 'feeders', 'neighborhood_transformers', 'premises', 'microgrids', 'assets', 'asset_components']
    LOOP
        SELECT string_agg(attname || ':' || format_type(atttypid, atttypmod), ',' ORDER BY attnum) || ','
               || 'operation:character varying(6),changed_by:uuid,actor_type:character varying(20),change_reason:character varying(255)'
        INTO expected
        FROM pg_attribute
        WHERE attrelid = format('topology.%I', t)::regclass AND attnum > 0 AND NOT attisdropped;

        SELECT string_agg(attname || ':' || format_type(atttypid, atttypmod), ',' ORDER BY attnum)
        INTO actual
        FROM pg_attribute
        WHERE attrelid = format('topology.%I', t || '_history')::regclass AND attnum > 0 AND NOT attisdropped;

        IF expected IS DISTINCT FROM actual THEN
            RAISE EXCEPTION 'topology.%_history columns do not match topology.% (expected %, got %)', t, t, expected, actual;
        END IF;

        SELECT count(*) INTO trigger_count
        FROM pg_trigger
        WHERE tgrelid = format('topology.%I', t)::regclass AND tgname = 'trg_' || t || '_versioning' AND NOT tgisinternal;

        IF trigger_count <> 1 THEN
            RAISE EXCEPTION 'topology.% is missing its versioning trigger', t;
        END IF;
    END LOOP;
END $$;
