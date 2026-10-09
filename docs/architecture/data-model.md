# Data Model: `rva_core`

Entity-relationship diagrams for `infrastructure/postgres/core/rva_core_init.sql`, the core database's schema. Each backend module owns one schema, and **no foreign keys cross schemas**, so each schema has its own diagram. A third diagram shows the references between modules, which are plain IDs and codes checked by the owning module's contracts.

The DDL is the source of truth. Update this page in the same pull request whenever the schema changes.

**Notation:** `||` exactly one, `|o` zero or one, `o{` zero or more. A solid line is a foreign key; a dotted line is a reference with no foreign key. Column types are simplified: `varchar` lengths, `geography(Point, 4326)`, and `geometry(LineString, 4326)` are shortened.

## `topology` schema (GridTopology module)

```mermaid
erDiagram
    technology_types {
        int technology_type_id PK
        varchar internal_code UK "bess, solar_pv, wind, evse"
        varchar display_name
        timestamptz created_at
        tstzrange sys_period
    }
    protocol_mappings {
        bigint protocol_mapping_id PK
        int technology_type_id FK
        varchar protocol_standard UK "OpenADR_3.0, IEEE_2030_5; unique with category and type"
        varchar resource_category UK
        varchar asset_type_code UK
        jsonb extra_attributes
        timestamptz created_at
        tstzrange sys_period
    }
    interconnection_types {
        int interconnection_type_id PK
        varchar internal_code UK "btm, ftm, atm"
        varchar display_name
        timestamptz created_at
        tstzrange sys_period
    }
    customer_segments {
        int customer_segment_id PK
        varchar internal_code UK "residential, commercial_industrial"
        varchar display_name
        timestamptz created_at
        tstzrange sys_period
    }
    grid_node_types {
        int grid_node_type_id PK
        varchar internal_code UK "substation, feeder, neighborhood_transformer, premise, microgrid"
        varchar display_name
        timestamptz created_at
        tstzrange sys_period
    }
    grid_nodes {
        bigint grid_node_id PK "one id space across node levels"
        int grid_node_type_id FK
        timestamptz created_at
    }
    hardware_profiles {
        int hardware_profile_id PK
        varchar hardware_profile_code UK "e.g. tesla_powerwall_3"
        varchar manufacturer "selects the integration adapter"
        varchar model
        int technology_type_id FK "NULL for control equipment"
        double rated_kw "per unit"
        double rated_kva "per unit"
        double rated_kwh "per unit, storage only"
        double round_trip_efficiency
        timestamptz created_at
        tstzrange sys_period
    }
    substations {
        bigint substation_id PK
        bigint grid_node_id UK, FK "assigned by trigger"
        varchar substation_code UK
        double rated_capacity_kva
        geography location
        timestamptz created_at
        tstzrange sys_period
    }
    feeders {
        bigint feeder_id PK
        bigint grid_node_id UK, FK "assigned by trigger"
        bigint substation_id FK
        varchar feeder_code UK
        double nominal_voltage_kv "e.g. 12.47"
        double rated_capacity_kva "optional"
        geometry line_geometry
        timestamptz created_at
        tstzrange sys_period
    }
    neighborhood_transformers {
        bigint neighborhood_transformer_id PK
        bigint grid_node_id UK, FK "assigned by trigger"
        bigint feeder_id FK
        varchar neighborhood_transformer_code UK
        double rated_capacity_kva
        double secondary_voltage_v "e.g. 240"
        geography location
        timestamptz created_at
        tstzrange sys_period
    }
    premises {
        bigint premise_id PK
        bigint grid_node_id UK, FK "assigned by trigger"
        bigint neighborhood_transformer_id FK "secondary service; exactly one of transformer or feeder"
        bigint feeder_id FK "primary service"
        varchar premise_code UK
        int customer_segment_id FK
        double service_capacity_kva "site import limit"
        double export_limit_kw "site export limit; 0 = non-export"
        boolean can_island "behind-the-meter microgrid"
        varchar address
        geography location
        timestamptz created_at
        tstzrange sys_period
    }
    microgrids {
        bigint microgrid_id PK
        bigint grid_node_id UK, FK "assigned by trigger"
        varchar microgrid_code UK
        bigint feeder_id FK "exactly one of feeder or substation"
        bigint substation_id FK
        boolean can_island
        double import_limit_kw
        double export_limit_kw
        timestamptz created_at
        tstzrange sys_period
    }
    assets {
        bigint asset_id PK
        varchar asset_code UK
        bigint premise_id FK "exactly one of premise, transformer, feeder, substation"
        bigint neighborhood_transformer_id FK
        bigint feeder_id FK
        bigint substation_id FK
        bigint microgrid_id FK "optional membership"
        int interconnection_type_id FK
        int control_hardware_profile_id FK "control interface"
        double rated_p_min_kw "negative = charging"
        double rated_p_max_kw
        double rated_kva
        double export_limit_kw "per-system limit"
        timestamptz created_at
        tstzrange sys_period
    }
    asset_components {
        bigint asset_component_id PK
        bigint asset_id FK "cascade delete"
        int technology_type_id FK "must match the profile's technology"
        int hardware_profile_id FK
        int unit_count
        double rated_kw "installed total"
        double rated_kwh "installed total"
        timestamptz created_at
        tstzrange sys_period
    }
    outbox_messages {
        bigint outbox_message_id PK
        uuid event_id UK
        varchar event_type "e.g. AssetUpdated"
        jsonb payload
        timestamptz occurred_at
        timestamptz published_at "NULL until delivered"
        int attempt_count
    }

    technology_types ||--o{ protocol_mappings : "maps to protocols"
    technology_types |o--o{ hardware_profiles : "classifies"
    technology_types ||--o{ asset_components : "typed as"
    hardware_profiles |o--o{ asset_components : "specifies"
    hardware_profiles |o--o{ assets : "controls"
    interconnection_types ||--o{ assets : "meter position"
    customer_segments ||--o{ premises : "service class"
    substations ||--o{ feeders : "serves"
    feeders ||--o{ neighborhood_transformers : "serves"
    neighborhood_transformers |o--o{ premises : "serves (secondary)"
    feeders |o--o{ premises : "serves (primary)"
    grid_node_types ||--o{ grid_nodes : "classifies"
    grid_nodes ||--o| substations : "identifies"
    grid_nodes ||--o| feeders : "identifies"
    grid_nodes ||--o| neighborhood_transformers : "identifies"
    grid_nodes ||--o| premises : "identifies"
    grid_nodes ||--o| microgrids : "identifies"
    substations |o--o{ microgrids : "connects"
    feeders |o--o{ microgrids : "connects"
    substations |o--o{ assets : "connects"
    feeders |o--o{ assets : "connects"
    neighborhood_transformers |o--o{ assets : "connects"
    premises |o--o{ assets : "connects"
    microgrids |o--o{ assets : "groups"
    assets ||--o{ asset_components : "made of"
```

**Rules the diagram can't show:**
- **Connection points:** an asset connects at exactly one of a premise, neighborhood transformer, feeder, or substation; a premise at exactly one of a neighborhood transformer (secondary service) or feeder (primary service); a microgrid at exactly one of a feeder or substation (`CHECK` constraints). Parent links record the normal configuration, not the energized path.
- **Grid node identity:** inserting a substation, feeder, neighborhood transformer, premise, or microgrid creates its `grid_nodes` row by trigger, of the matching type. `grid_node_id` can't be changed, and `grid_nodes` rejects `UPDATE` and `DELETE`, so a removed node's id stays registered for data that refers to it.
- **Components and profiles:** `asset_components (hardware_profile_id, technology_type_id)` is a composite foreign key, so a component can only use an energy product of its own technology, never control-only equipment.
- **Asset power:** an asset's real power can't exceed its kVA rating.
- **Versioning:** every table above except `grid_nodes` (never changes) and `outbox_messages` is system-versioned by `topology.enable_versioning`. Each has a `<table>_history` twin with the same columns (matched by name) plus `operation`, `changed_by` (UUID), `actor_type`, and `change_reason`, and a `<table>_as_of(point_in_time)` function. The 13 history tables are left out of the diagram, since each mirrors its main table.
- **Rules enforced in C# instead:** transformer-, feeder-, and substation-connected assets are `ftm`; an asset has at least one component; microgrid members sit under the microgrid's own feeder.

## `simulation` schema (Simulation module)

```mermaid
erDiagram
    simulation_run_status_types {
        int status_type_id PK
        varchar name UK "Pending, Running, Completed, Failed"
        varchar description
    }
    scope_levels {
        int scope_level_id PK
        varchar internal_code UK "substation, feeder, neighborhood_transformer, premise"
        varchar display_name
    }
    detail_levels {
        int detail_level_id PK
        varchar internal_code UK "hierarchy, assets, assets_with_components"
        varchar display_name
    }
    run_kinds {
        int run_kind_id PK
        varchar internal_code UK "chaos; twin kinds added later"
        varchar display_name
    }
    simulation_runs {
        uuid simulation_run_id PK "uuidv7"
        varchar scenario_name
        int run_kind_id FK
        int status_type_id FK "current status"
        int scope_level_id FK
        varchar scope_root_code "resolved through GridTopology"
        int detail_level_id FK
        timestamptz topology_as_of "topology version used"
        tstzrange simulated_window "simulated time"
        timestamptz initial_state_as_of "start from measured state; NULL = synthetic"
        jsonb configuration_json "refers to assets by code"
        timestamptz created_at
        timestamptz started_at "NULL while Pending"
        timestamptz ended_at
        timestamptz updated_at
    }
    simulation_run_events {
        bigint simulation_run_event_id PK
        uuid simulation_run_id FK
        int status_type_id FK
        timestamptz occurred_at
        varchar reason
        uuid actor_id
        varchar actor_type "user, service, system"
        jsonb details
    }
    simulation_asset_states {
        bigint simulation_asset_state_id PK
        uuid simulation_run_id FK "unique with asset_id"
        bigint asset_id "GridTopology asset; no FK"
        boolean is_available
        double p_min_kw "NULL = no override"
        double p_max_kw
        double state_of_charge_pct
        timestamptz created_at
    }
    outbox_messages {
        bigint outbox_message_id PK
        uuid event_id UK
        varchar event_type "e.g. SimulationRunStarted"
        jsonb payload
        timestamptz occurred_at
        timestamptz published_at
        int attempt_count
    }

    simulation_run_status_types ||--o{ simulation_runs : "current status"
    simulation_run_status_types ||--o{ simulation_run_events : "status entered"
    scope_levels ||--o{ simulation_runs : "scope level"
    detail_levels ||--o{ simulation_runs : "detail level"
    run_kinds ||--o{ simulation_runs : "kind"
    simulation_runs ||--o{ simulation_run_events : "lifecycle history"
    simulation_runs ||--o{ simulation_asset_states : "starting-state overrides"
```

**Rules the diagram can't show:**
- **Run events are written by trigger** whenever a run is created or changes status, and the table rejects `UPDATE` and `DELETE`. Because events reference their run, a run with events can't be deleted.
- **Run inputs are fixed:** `run_kind_id`, `topology_as_of`, `initial_state_as_of`, and `created_at` can't change after creation, and overrides can't be inserted, changed, or deleted once a run leaves `Pending`.
- **Overrides are starting state only,** at most one per asset per run. Changes during a run belong in `configuration_json` or a future per-run event table.
- **Timestamps:** `started_at >= created_at`, `ended_at` after both (a run can fail before it starts), and `topology_as_of` and `initial_state_as_of` are no later than `created_at`.

## References between modules (no foreign keys)

```mermaid
erDiagram
    assets ||..o{ simulation_asset_states : "asset_id"
    substations ||..o{ simulation_runs : "scope_root_code"
    feeders ||..o{ simulation_runs : "scope_root_code"
    neighborhood_transformers ||..o{ simulation_runs : "scope_root_code"
    premises ||..o{ simulation_runs : "scope_root_code"
```

Simulation stores these references as plain values. `CreateRun` checks them through `IGridTopologyQueries`: it validates the scope root against the topology version given by `topology_as_of`, and turns override `asset_code`s into `asset_id`s. Telemetry's tables live in a separate database (`rva_telemetry`) and refer to `asset_id` the same way.
