# Implementation Plan: Phase 1 (Ingestion, Geospatial & Chaos)

This plan builds the backend foundation and the first three modules. It is organized **by module (vertical slice), not by layer**: each step delivers one module end to end — domain, persistence, features, contracts, and tests. Architecture rules are in `.ai/rules/csharp-monolith.md`; this plan does not repeat them.

**Cross-cutting decisions:**
* **Hybrid keys:** Entities use a surrogate `long` ID (`BIGINT GENERATED ALWAYS AS IDENTITY`) for internal relationships and simulator indexing, plus a natural `Code` string with a unique constraint for idempotent seeding and external references. Lookup tables use `int` IDs (`INTEGER`).
* **Naming:** Primary keys are `{entity}_id`, natural codes are `{entity}_code`, and foreign keys use the referenced primary key's exact name. C# properties are `Id` and `Code`.
* **Lookup tables, not enums:** Constrained values (technology type, interconnection type, customer segment, run status, run scope and detail level) are lookup tables enforced by foreign keys *within* a module. No PostgreSQL `ENUM` types.
* **Change tracking:** state that gets edited is system-versioned (topology); things that happen are append-only events (simulation, telemetry); each module has an outbox for integration events. See `.ai/rules/csharp-monolith.md` section 9.
* **Identity:** user and service IDs are UUIDv7, owned by a later Identity module (Phase 3, OIDC). Until then, changes are attributed to a fixed system actor or left unattributed.
* **One utility per deployment:** a deployment holds exactly one utility's topology. A second utility is a separate deployment (its own host, databases, Redis prefix, and MQTT topic root), so database names, prefixes, and topic roots come from configuration.
* **Phase 1 device integration is MQTT only.** Manufacturer cloud adapters (Tesla, Enphase, …) are a later phase that needs per-manufacturer research.
* **Data ownership:**

| Module | Storage | Owns |
|---|---|---|
| GridTopology | `rva_core`, schema `topology` | `technology_types`, `protocol_mappings`, `interconnection_types`, `customer_segments`, `hardware_profiles`, `substations`, `feeders`, `neighborhood_transformers`, `premises`, `microgrids`, `assets`, `asset_components` (each with a `_history` twin), `outbox_messages` |
| Simulation | `rva_core`, schema `simulation` | `simulation_run_status_types`, `scope_levels`, `detail_levels`, `simulation_runs`, `simulation_run_events`, `simulation_asset_states`, `outbox_messages` |
| Telemetry | `rva_telemetry` | `known_assets` (read model), `asset_telemetry` (hypertable) |

---

## Step 0: Backend Foundation

**Goal:** A buildable, testable skeleton that enforces the module rules before any feature code exists.

Delivered in parts, one branch and pull request each:

| Part | Scope | Status |
|---|---|---|
| `p1-s00a` | Project skeleton with the allowed references, and the architecture test suite | Done (PR #3) |
| `p1-s00b` | Host wiring: module registration, Serilog, the in-process event publisher, configurable database names / Redis prefix / MQTT topic root | Not started |
| `p1-s00c` | Dockerfiles for `Rva.Host` (and the simulator and sidecar profiles) and a CI workflow running build, test, and the schema smoke test | Not started |

* **Solution:** Add `Rva.Host`, `Rva.SharedKernel`, and the `Rva.Modules.<X>` / `Rva.Modules.<X>.Contracts` project pairs to `VppAggregator.slnx`, with only the references allowed by the rule.
* **Host (`Rva.Host`):** ASP.NET Core host — HTTP endpoints plus background workers (e.g. MQTT ingestion). Each module exposes `Add<X>Module(IServiceCollection, IConfiguration)` and `Map<X>Endpoints(IEndpointRouteBuilder)`; the host calls these and nothing else module-specific. Serilog configured here.
* **Shared kernel (`Rva.SharedKernel`):** Starts with only the types Step 1's contracts need; anything else is added when a second module needs it (see the rule's section 5). Expected over Phase 1: typed IDs (`AssetId`, `SubstationId`, `FeederId`, `TransformerId`, `PremiseId`, `MicrogridId`, `SimulationRunId`), units (`Kilowatts`, `KilowattHours`, `KilovoltAmperes`, `Kilovars`, `Volts`, `Percentage`), reference codes (`TechnologyTypeCode`, `InterconnectionTypeCode`, `ProtocolStandard`), `Result`/`Result<T>`/`Error`, `IIntegrationEvent`.
* **Integration events:** An in-process event publisher in `Rva.Host` (e.g. a `Channel`-backed dispatcher) that modules publish to via an abstraction; handlers live in the consuming module.
* **Architecture tests:** Reference every backend project from `tests/Rva.ArchitectureTests` and implement the full suite listed in the rule's section 8.
* **Schema smoke test:** `infrastructure/postgres/tests/rva_core_smoke_test.sql` exercises versioning, as-of reads, actor/reason capture, key constraints, and the simulation triggers (see `.ai/scripts/verify.md`).
* **Infrastructure:** `docker compose up` brings up `rva-core-db`, `rva-telemetry-db`, Mosquitto (with a mounted `mosquitto.conf`), and Redis on one network, and runs each database's init script.

---

## Step 1: GridTopology Module (`Rva.Modules.GridTopology`)

**Goal:** Own the physical grid model: the hierarchy and its ratings, customer sites, microgrids, assets and what they are made of, and the technology/interconnection taxonomy, including how that taxonomy maps to external protocols. The schema is in `infrastructure/postgres/core/rva_core_init.sql`.

* **Modeling principles:**
  * **An asset is a grid-facing controllable resource:** everything behind **one control interface**, at **one connection point**, with **one rating at that point**. A DC-coupled Powerwall 3 home with PV is one asset; an Enphase PV + battery system behind one IQ Gateway is one asset; Tesla batteries plus SolarEdge PV at the same home are two assets.
  * **An asset's rating is not the sum of its components.** Dispatch uses asset ratings; the simulator uses component ratings for physics.
  * **A premise is a customer site behind one meter** (the point of common coupling). A behind-the-meter microgrid is a premise with `CanIsland`; all assets at a premise island together.
  * **A microgrid row exists only for utility-side groupings** spanning several premises and/or feeder-connected assets. It connects at a feeder or substation, so its interconnection type is always `ftm` and is not stored.
  * **Limits exist at two levels:** site limits (premise or microgrid) and per-system limits (asset). Dispatch must respect both, plus the node ratings.
  * **Customer segment is the premise's service class and is independent of equipment size.** Size comes from ratings and hardware profiles, never from the technology type.
* **Domain (module-internal):**
  * **`TechnologyType`** (lookup): `Id` (`int`), `InternalCode` (`bess`, `solar_pv`, `wind`, `evse`), `DisplayName`. Technology only — external protocol codes live in `ProtocolMapping`.
  * **`ProtocolMapping`** (lookup): `Id` (`long`), `TechnologyTypeId`, `ProtocolStandard` (e.g. `OpenADR_3.0`, `IEEE_2030_5`), `ResourceCategory` (e.g. `Storage`), `AssetTypeCode` (e.g. `Battery`), `ExtraAttributes` (JSON). One technology type can have one row per external standard it speaks. Keeps protocol-to-internal mappings in data rather than hardcoded in application code.
  * **`InterconnectionType`** (lookup): `Id` (`int`), `InternalCode` (`btm`, `ftm`, `atm`), `DisplayName`. Meter position only. Voltage level comes from the connection node, islanding is premise/microgrid capability plus runtime state, and virtual metering belongs to program enrollment (later phase).
  * **`CustomerSegment`** (lookup): `Id` (`int`), `InternalCode` (`residential`, `commercial_industrial`), `DisplayName`.
  * **`HardwareProfile`:** `Id` (`int`), `Code` (e.g. `tesla_powerwall_3`), `Manufacturer`, `Model`, `TechnologyTypeId` (null = conversion/control equipment such as inverters, gateways, plant controllers), per-unit `RatedKw` / `RatedKva` / `RatedKwh`, `RoundTripEfficiency`. The manufacturer of an asset's control profile selects its device-integration adapter.
  * **Hierarchy:** `Substation` (`RatedCapacityKva`) → `Feeder` (`NominalVoltageKv`, optional `RatedCapacityKva`) → `NeighborhoodTransformer` (`RatedCapacityKva`, `SecondaryVoltageV`) → `Premise`. Each has `Id` (`long`), `Code`, a typed parent ID, and PostGIS location/geometry. Node ratings are required: unknown capacity must never mean "unconstrained." The four entities may inherit a module-internal abstract `GridNode` base class (`Id`, `Code`, `Location`) for level-agnostic code such as tree building and spatial lookups. `GridNode` is **not** a table and is **not** mapped as EF Core inheritance — each level's IDs come from its own table's `IDENTITY`, so IDs are not unique across levels.
  * **`Premise`:** `CustomerSegmentId`, `ServiceCapacityKva` (import limit), `ExportLimitKw` (site interconnection limit; `0` = non-export), `CanIsland`.
  * **`Microgrid`:** `Id` (`long`), `Code`, connection at exactly one of `FeederId` / `SubstationId`, `CanIsland`, `ImportLimitKw`, `ExportLimitKw`.
  * **`Asset`** (aggregate root, owns its components): `Id` (`long`), `Code`, connection at exactly one of `PremiseId` / `FeederId` / `SubstationId`, `MicrogridId` (`long?`), `InterconnectionTypeId`, `ControlHardwareProfileId`, `RatedPMinKw` / `RatedPMaxKw` (negative = charging/import), `RatedKva`, `ExportLimitKw` (per system). Makeup (PV only, storage, PV + storage) and energy capacity are derived from its components.
  * **`AssetComponent`:** `TechnologyTypeId`, `HardwareProfileId`, `UnitCount`, installed `RatedKw` (PV kW-dc or battery power) and `RatedKwh`. Never dispatched directly.
* **Invariants enforced in the entities** (they need cross-table checks the database cannot express):
  * Feeder- or substation-connected assets are `ftm`.
  * An asset has at least one component.
  * Microgrid members sit under the microgrid's own feeder (or substation).
  * A component's technology matches its hardware profile (the database also enforces this with a composite foreign key).
* **Change tracking (system-versioned):** every topology table has `created_at` and `sys_period`, a `<table>_history` twin, a versioning trigger, and a `<table>_as_of(timestamptz)` function. `lower(sys_period)` doubles as the last-updated time. The init script verifies that every history table matches its main table.
  * **Persistence:** EF Core maps the main tables as usual. `created_at` and `sys_period` are database-generated (never written by EF), and history tables are not mapped as entities.
  * **Who and why:** a `Persistence/` interceptor runs `SET LOCAL rva.actor`, `rva.actor_type`, and `rva.change_reason` at the start of each command's transaction, so every topology change runs in an explicit transaction.
  * **As-of reads** use `FromSql` against the `_as_of` functions.
  * **Outbox:** integration events are written to `topology.outbox_messages` in the same transaction as the change, and delivered by a background publisher.
* **Schema (`topology`):** as defined in `rva_core_init.sql`. Key database-level rules:
  * single-connection `CHECK`s on assets (`num_nonnulls(premise_id, feeder_id, substation_id) = 1`) and microgrids (`feeder_id` / `substation_id`)
  * asset power within kVA (`GREATEST(ABS(rated_p_min_kw), ABS(rated_p_max_kw)) <= rated_kva`)
  * composite FK `asset_components (hardware_profile_id, technology_type_id)` → `hardware_profiles`, so a component can only use an energy product of its own technology
  * `asset_components.asset_id` cascades on delete; all other topology FKs restrict
* **Seeding:** Idempotent seed of lookups (including protocol mappings) and hardware profiles, keyed on natural codes (`ON CONFLICT (…_code) DO NOTHING`), plus a sample Portland topology for development. The four reference scenarios are **test fixtures**, seeded into a test database, never mixed into the development topology:
  * **A:** home with a DC-coupled Powerwall 3 + PV (one asset, two components, inverter-capped rating).
  * **B:** non-export C&I warehouse with separate PV and battery systems (`premises.export_limit_kw = 0`, behind-the-meter microgrid via `can_island`).
  * **C:** residential home with three Powerwall 3s and a 10 kW site export cap (large equipment, still `residential`).
  * **D:** community microgrid on a feeder — 20 homes (Tesla DC-coupled, Enphase single-gateway, PV-only, and a mixed-vendor home with two assets), a school, and a feeder-connected community battery — including a neighborhood transformer whose connected inverter capacity exceeds its rating.
* **Features:** `RegisterAsset`, `GetAsset`, `GetSubstationTree`, `GetTopologySnapshot(scopeRoot, detail, asOf)` (bulk export of a subtree at a chosen level of detail and topology version, used by `Rva.Simulator`).
* **Contracts:** `IGridTopologyQueries` (`GetAsset(id, asOf?)`, `GetAssetsBySubstation`, `GetTopologySnapshot(scopeRoot, detail, asOf)`, `ResolveTechnologyType`, `GetProtocolMappings`), `AssetSnapshot` (connection point and substation, ratings, site and microgrid limits, makeup, control manufacturer, `ValidFrom`), `ProtocolMappingSnapshot`, and the events `AssetRegistered`, `AssetUpdated`, `AssetDecommissioned`.

---

## Step 2: Simulation Module (`Rva.Modules.Simulation`)

**Goal:** Own the simulation run ledger and per-run asset overrides. This module does **not** run the Chaos Engine — `Rva.Simulator` does (Step 4).

* **Domain:** `SimulationRun` (lifecycle `Pending` → `Running` → `Completed` | `Failed`, with transitions enforced by the entity), `SimulationAssetState` (per-run override of an asset's availability and power bounds).
* **Change tracking (append-only events):** simulation data is things that happen, so it is recorded as events rather than versioned:
  * every status a run enters is written to `simulation_run_events` by trigger, with the reason and actor from `SET LOCAL`; the table rejects updates and deletes, and a run with events cannot be deleted
  * run inputs (scope, `topology_as_of`, overrides) are fixed once a run leaves `Pending`, enforced in the database, so a run can be replayed exactly
* **Scope:** each run records which part of the grid it covers (`scope_level_id` + `scope_root_code`, e.g. `neighborhood_transformer` / `XFMR-07-A`), how much detail it loads (`detail_level_id`: hierarchy, assets, or assets with components), and which topology version it uses (`topology_as_of`, defaulting to the run's creation time).
* **Schema (`simulation`):** as defined in `rva_core_init.sql`:
  * `simulation_runs`: `created_at`, `started_at` (NULL while Pending), `ended_at`, `updated_at`, optional `simulated_window` (simulated time, independent of wall-clock execution), the scope columns above, `configuration_json` (refers to assets by `asset_code`)
  * `simulation_run_events`: append-only lifecycle history
  * `simulation_asset_states`: per-run overrides, `asset_id` with **no FK** (GridTopology owns assets), changeable only while the run is Pending
  * `outbox_messages`: integration events (`SimulationRunStarted`, `SimulationRunEnded`)
* **Asset references:** Scenario configuration and API requests identify assets by `asset_code`, which is portable across environments. `CreateRun` resolves codes to `asset_id` through `IGridTopologyQueries` and rejects unknown codes; overrides are stored by `asset_id`.
* **Features:** `CreateRun` (validates the scope root and override asset codes through `IGridTopologyQueries`), `StartRun`, `CompleteRun`, `FailRun`, `GetRun`.
* **Contracts:** `ISimulationQueries`, and the events `SimulationRunStarted` and `SimulationRunEnded`.

---

## Step 3: Telemetry Module (`Rva.Modules.Telemetry`)

**Goal:** Ingest high-throughput device telemetry over MQTT through the ACL into TimescaleDB.

* **Broker & envelope:** Eclipse Mosquitto; every message is a **CloudEvents** JSON envelope (`specversion`, `type`, `source`, `id`, `time`, `data`), with `source` carrying the device's `asset_code`.
* **Schema (`rva_telemetry`):**
  * `known_assets` (`asset_id` BIGINT PK, `asset_code` VARCHAR UNIQUE NOT NULL, `control_manufacturer` VARCHAR(100) NULL). A local read model maintained from GridTopology events; `control_manufacturer` selects the device-integration adapter when manufacturer-cloud ingestion is added.
  * `asset_telemetry` hypertable partitioned by `timestamp`: `timestamp` TIMESTAMPTZ NOT NULL, `asset_id` BIGINT NOT NULL, `simulation_run_id` UUID NULL, `active_power_kw`, `reactive_power_kvar`, `state_of_charge_pct`, `voltage_v` (DOUBLE PRECISION). No FKs (the other tables are in another database).
* **Pipeline:** MQTT listener → bounded `Channel` → ACL (resolves `asset_code` → `AssetId` against the in-memory `known_assets` cache; rejects unknown devices) → batched bulk insert.
* **Features:** `IngestDeviceTelemetry`, `GetAssetHistory`, and the integration-event handlers that maintain `known_assets`.
* **Contracts:** `ITelemetryQueries` (latest reading per asset — used later by the Reconciliation Loop).

---

## Step 4: Chaos Engine (`src/Rva.Simulator`, external process)

**Goal:** Simulate a 50,000-node device fleet that exercises the backend only through its public interfaces.

* **Bootstrapping:** Load the run configuration from `Simulation`'s `GetRun` endpoint, then the topology from `GridTopology`'s `GetTopologySnapshot` endpoint using the run's scope root, detail level, and `topology_as_of`. There is no direct database access.
* **Physics:** Use asset components for behavior (PV kW-dc follows weather, battery kWh and round-trip efficiency drive state of charge) and asset ratings as hard caps at the connection point (a shared inverter limits PV + battery together).
* **Identity mapping:** Map assets into contiguous arrays (`int` internal index ↔ `long` `AssetId`) for zero-allocation ticks.
* **Ticks:** Apply `simulation_asset_states` overrides and `configuration_json` fault rules (thermal, voltage, comms) per tick; publish CloudEvents telemetry to Mosquitto; report run start/completion/failure through the Simulation endpoints.

---

## Phase 1 Definition of Done

* [ ] `dotnet build` and `dotnet test` pass, including the full architecture test suite.
* [ ] `docker compose up` starts both databases, Mosquitto, and Redis, and runs the init scripts cleanly; the schema smoke test passes.
* [ ] Seed scripts can be re-run without duplicating rows (natural-code idempotency), and the reference scenarios A–D load with all database constraints passing.
* [ ] GridTopology serves the topology and publishes asset events; Telemetry's `known_assets` stays in sync.
* [ ] Simulation runs move through their lifecycle via the API, with overrides validated against GridTopology.
* [ ] `Rva.Simulator` drives 50,000 nodes and its CloudEvents telemetry lands in `asset_telemetry` through the ACL.

---

## Deferred (recorded, not in Phase 1)

* **What-if overlays:** generalize `simulation_asset_states` into per-run overlays that add, remove, or modify assets and limits on top of the base topology. Phase 1 uses only per-asset overrides.
* **Premise load:** a load profile reference and peak kW on premises, so transformer loading reflects net load (consumption minus DER output). Phase 1 compares inverter capacity with node ratings.
* **Manufacturer cloud adapters:** device integration beyond MQTT; `hardware_profiles.manufacturer` and `known_assets.control_manufacturer` are already in place to route them.
* **Audit module timing and dispatch phases:** scheduling the Audit module before the first grid-impacting command, and adding Portfolio/Enrollment and Dispatch phases, is a separate roadmap change to `MASTER_PLAN.md`.
