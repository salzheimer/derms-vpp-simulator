# Implementation Plan: Phase 1 (Ingestion, Geospatial & Chaos)

This plan builds the backend foundation and the first three modules. It is organized **by module (vertical slice), not by layer**: each step delivers one module end to end — domain, persistence, features, contracts, and tests. Architecture rules are in `.ai/rules/csharp-monolith.md`; this plan does not repeat them.

**Cross-cutting decisions:**
* **Hybrid keys:** Entities use a surrogate `long` ID (`BIGINT GENERATED ALWAYS AS IDENTITY`) for internal relationships and simulator indexing, plus a natural `Code` string with a unique constraint for idempotent seeding and external references. Lookup tables use `int` IDs (`INTEGER`).
* **Naming:** Primary keys are `{entity}_id`, natural codes are `{entity}_code`, and foreign keys use the referenced primary key's exact name. C# properties are `Id` and `Code`.
* **Lookup tables, not enums:** Constrained values (technology type, interconnection type, customer segment, run status, run scope and detail level) are lookup tables enforced by foreign keys *within* a module. No PostgreSQL `ENUM` types.
* **Change tracking:** state that gets edited is system-versioned (topology); things that happen are append-only events (simulation, telemetry); each module has an outbox for integration events. See `.ai/rules/csharp-monolith.md` section 9.
* **Identity:** user and service IDs are UUIDv7, owned by a later Identity module (Phase 3, OIDC). Until then, changes are attributed to a fixed system actor or left unattributed.
* **One utility per deployment:** a deployment holds exactly one utility's topology. A second utility is a separate deployment (its own host, databases, Redis prefix, and MQTT topic root), so database names, prefixes, and topic roots come from configuration.
* **Digital twin readiness:** a later phase may add a digital twin (an electrical network model, power flow, and forecast runs seeded from live state). Phase 1 fixes the structure that would otherwise have to change then, so the twin is added through new tables, columns, and lookup rows only:
  * **One id space for grid nodes:** every substation, feeder, neighborhood transformer, premise, and microgrid has a `grid_node_id` from `topology.grid_nodes`. Node-keyed data (operating envelopes, node measurements, network model, per-node simulation results) references it.
  * **Hierarchy foreign keys are the normal configuration,** not the energized path. A future network model (buses, line segments, switches) references the hierarchy tables, never the reverse.
  * **Constraint checks never walk hierarchy foreign keys themselves.** They ask GridTopology which nodes constrain an asset (`GetConstraintPath`), so switching can later change the answer without changing callers.
  * **Limits are evaluated behind one interface.** Phase 1 compares capacity with static node ratings; a power-flow evaluator can replace it behind the same `OperatingEnvelope` contract.
  * **Versioned tables can gain columns:** history rows are archived by column name, and `topology.enable_versioning(<table>)` is re-run after any `ALTER TABLE`.
  * **Simulation runs have a kind** (`chaos` now; forecast, what-if, and replay later) and can start from measured state (`initial_state_as_of`).
  * **Prerequisite, not yet in place:** the init scripts run only on an empty database. Upgrading a live database needs a migration tool, which is required before the first change to a deployed schema.
* **Phase 1 device integration is MQTT only.** Manufacturer cloud adapters (Tesla, Enphase, …) are a later phase that needs per-manufacturer research.
* **Data ownership:**

| Module | Storage | Owns |
|---|---|---|
| GridTopology | `rva_core`, schema `topology` | `technology_types`, `protocol_mappings`, `interconnection_types`, `customer_segments`, `grid_node_types`, `hardware_profiles`, `substations`, `feeders`, `neighborhood_transformers`, `premises`, `microgrids`, `assets`, `asset_components` (each with a `_history` twin), `grid_nodes` (append-only identity registry), `outbox_messages` |
| Simulation | `rva_core`, schema `simulation` | `simulation_run_status_types`, `scope_levels`, `detail_levels`, `run_kinds`, `simulation_runs`, `simulation_run_events`, `simulation_asset_states`, `outbox_messages` |
| Telemetry | `rva_telemetry` | `known_assets` (read model), `asset_telemetry` (hypertable) |

---

## Step 0: Backend Foundation

**Goal:** A buildable, testable skeleton that enforces the module rules before any feature code exists.

Delivered in parts, one branch and pull request each:

| Part | Scope | Status |
|---|---|---|
| `p1-s00a` | Project skeleton with the allowed references, and the architecture test suite | Done (PR #3) |
| `p1-s00b` | Host wiring: module registration, Serilog, the in-process event publisher, configurable database names / Redis prefix / MQTT topic root (see the `p1-s00b` acceptance criteria below) | Done (PR #11) |
| `p1-s00c` | Dockerfiles for `Rva.Host` (and the simulator and sidecar profiles), the sidecar dependency refresh for Python 3.12, and a CI workflow running build and test (the database tests have their own workflow from `p1-s00f`) (see the `p1-s00c` acceptance criteria below) | Done (PR #14) |
| `p1-s00d` | Digital twin readiness in `rva_core`: grid node identity, wider connection points, column-safe versioning, simulation run kinds; plan and doc rules (see Cross-cutting decisions) | Done (PR #7) |
| `p1-s00e` | Self-describing schema: table and column comments throughout `rva_core` (generated for history tables), the SQL DDL standard (`.ai/rules/sql-ddl.md`), and a catalog-based DDL lint (`infrastructure/postgres/tests/ddl_lint.sql`) | Done (PR #8) |
| `p1-s00f` | Executable schema tests: pgTAP suite for the GridTopology and Simulation scenarios (`docs/architecture/model-validation-scenarios.md`), reusable fixtures A–D, the smoke test ported to pgTAP, a test runner and CI job; asset lifecycle, program participation, and grid state events in `rva_core` (see `.ai/plans/P1_S00F_DDL_TESTS_PLAN.md`) | Done (PR #9) |

* **Solution:** Add `Rva.Host`, `Rva.SharedKernel`, and the `Rva.Modules.<X>` / `Rva.Modules.<X>.Contracts` project pairs to `VppAggregator.slnx`, with only the references allowed by the rule.
* **Host (`Rva.Host`):** ASP.NET Core host — HTTP endpoints plus background workers (e.g. MQTT ingestion). Each module exposes `Add<X>Module(IServiceCollection, IConfiguration)` and `Map<X>Endpoints(IEndpointRouteBuilder)`; the host calls these and nothing else module-specific. Serilog configured here.
* **Shared kernel (`Rva.SharedKernel`):** Starts with only the types Step 1's contracts need; anything else is added when a second module needs it (see the rule's section 5). Expected over Phase 1: typed IDs (`AssetId`, `GridNodeId`, `SubstationId`, `FeederId`, `TransformerId`, `PremiseId`, `MicrogridId`, `SimulationRunId`), units (`Kilowatts`, `KilowattHours`, `KilovoltAmperes`, `Kilovars`, `Volts`, `Percentage`), reference codes (`TechnologyTypeCode`, `InterconnectionTypeCode`, `ProtocolStandard`), `Result`/`Result<T>`/`Error`, `IIntegrationEvent`.
* **Integration events:** An in-process event publisher in `Rva.Host` (e.g. a `Channel`-backed dispatcher) that modules publish to via an abstraction; handlers live in the consuming module.
* **`p1-s00b` decisions:** the publish and handle interfaces (`IIntegrationEventPublisher`, `IIntegrationEventHandler<TEvent>`) live in `Rva.SharedKernel` beside `IIntegrationEvent`, as pure contracts; the channel dispatcher that implements them stays in `Rva.Host`. Deployment settings are one host-level `Rva:Deployment` options section (utility code, Redis key prefix, MQTT topic root) validated at startup, plus the `Core`, `Telemetry`, and `Redis` connection strings (database names live in the connection strings); each module reads only its own sections. Package versions are pinned centrally in the root `Directory.Packages.props`. The outbox relay (reading each module's `outbox_messages` and handing events to the dispatcher) is **not** part of `p1-s00b`: it arrives with the first module that publishes (Step 1, GridTopology). Until then, delivery is in-memory and best-effort.
* **`p1-s00b` acceptance criteria:**
  * `dotnet build VppAggregator.slnx` and `dotnet test VppAggregator.slnx` pass.
  * The host registers all three modules and maps their endpoints.
  * Serilog is configured by host-level settings from appsettings.
  * A channel-backed event publisher delivers an event to a handler registered by another module without an implementation-project reference between the modules.
  * Required deployment options fail startup with a clear validation error when missing or invalid.
  * The host binds the deployment settings and connection strings without implementing any outbox persistence or relay behavior.
* **`p1-s00c` decisions:** the .NET images (`src/Rva.Backend/Rva.Host/Dockerfile`, `src/Rva.Simulator/Dockerfile`) build from the repo root, because the build needs `global.json` and the central `Directory.Packages.props`; a root `.dockerignore` sends only those and the two source trees. The sidecar image builds from `src/Rva.Intelligence`. All images run as non-root. The host exposes `/health` as a liveness check only; readiness checks for databases, Redis, and MQTT arrive with the modules that use them. Sidecar dependencies are pinned to the newest release at least two weeks old that supports Python 3.12 (`aioredis` replaced by `redis.asyncio`); runtime packages stay in `requirements.txt` and test and lint tools move to `requirements-dev.txt` (pytest, pytest-asyncio, ruff, replacing black and flake8). The image installs CPU-only torch and runs uvicorn directly (gunicorn dropped until a worker model is chosen). The compose backend drops the `MqttSettings` and `AiSidecar` keys, which nothing reads yet; they return with the modules that read them. CI runs in a new `ci` workflow with no paths filter, beside `test-guardrail` and `db-tests`.
* **`p1-s00c` acceptance criteria:**
  * `dotnet build VppAggregator.slnx` and `dotnet test --solution VppAggregator.slnx` pass.
  * The backend, simulator, and sidecar images build with `docker build`; the backend and sidecar answer `GET /health` with 200 when started.
  * `docker compose --profile backend up` starts the backend against the stack and it answers `http://localhost:5000/health`.
  * The sidecar's `requirements.txt` installs on Python 3.12 with no broken requirements (`pip check`).
  * The `ci` workflow runs build, test, and the image builds and health checks on every pull request and push to `main`.
* **Architecture tests:** Reference every backend project from `tests/Rva.ArchitectureTests` and implement the full suite listed in the rule's section 8.
* **Schema tests:** the pgTAP suite in `infrastructure/postgres/tests/pgtap/` (run by `infrastructure/postgres/tests/run_tests.sh`) exercises versioning, as-of reads, actor/reason capture, key constraints, the simulation triggers, and each validation scenario (`p1-s00f`; see `.ai/scripts/verify.md`).
* **DDL lint:** `infrastructure/postgres/tests/ddl_lint.sql` checks the built schema against `.ai/rules/sql-ddl.md`; `run_tests.sh` runs it before the pgTAP suite, locally and in CI.
* **Infrastructure:** `docker compose up` brings up `rva-core-db`, `rva-telemetry-db`, Mosquitto (with a mounted `mosquitto.conf`), and Redis on one network, and runs each database's init script.

---

## Step 1: GridTopology Module (`Rva.Modules.GridTopology`)

**Goal:** Own the physical grid model: the hierarchy and its ratings, customer sites, microgrids, assets and what they are made of, and the technology/interconnection taxonomy, including how that taxonomy maps to external protocols. The schema is in `infrastructure/postgres/core/rva_core_init.sql`.

Delivered in parts, one branch and pull request each. The module structure, decisions, error handling, and the test catalogue for every part are in `.ai/plans/P1_S01_GRID_TOPOLOGY_PLAN.md`.

| Part | Scope | Status |
|---|---|---|
| `p1-s01a` | Foundation: test package decisions, shared kernel types, `DbContext` and mappings, change context, error classification, readiness check, test projects and the database test harness | Done (PR #16) |
| `p1-s01b` | Catalog and development seed (protocol mappings, hardware profiles, sample Portland topology); `ResolveTechnologyType`, `GetProtocolMappings` | Not started |
| `p1-s01c` | Reads: `GetAsset`, `GetAssetsBySubstation`, `GetSubstationTree`, `GetConstraintPath`, `ResolveNode`, as-of reads | Not started |
| `p1-s01d` | Grid state: `RecordGridStateEvent`, `GetGridState`, effective state and eligibility | Not started |
| `p1-s01e` | Asset writes: `RegisterAsset`, `ChangeAssetLifecycle`, `ChangeParticipation`, events written to the outbox | Not started |
| `p1-s01f` | Outbox relay delivering the integration events | Not started |
| `p1-s01g` | `GetTopologySnapshot` for `Rva.Simulator` | Not started |

* **Modeling principles:**
  * **An asset is a grid-facing controllable resource:** everything behind **one control interface**, at **one connection point**, with **one rating at that point**. A DC-coupled Powerwall 3 home with PV is one asset; an Enphase PV + battery system behind one IQ Gateway is one asset; Tesla batteries plus SolarEdge PV at the same home are two assets.
  * **An asset's rating is not the sum of its components.** Dispatch uses asset ratings; the simulator uses component ratings for physics.
  * **A premise is a customer site behind one meter** (the point of common coupling), served from a neighborhood transformer (secondary service, most premises) or directly from a feeder (primary service, where the customer owns its transformer). A behind-the-meter microgrid is a premise with `CanIsland`; all assets at a premise island together.
  * **A microgrid row exists only for utility-side groupings** spanning several premises and/or feeder-connected assets. It connects at a feeder or substation, so its interconnection type is always `ftm` and is not stored.
  * **Limits exist at two levels:** site limits (premise or microgrid) and per-system limits (asset). Dispatch must respect both, plus the node ratings.
  * **Customer segment is the premise's service class and is independent of equipment size.** Size comes from ratings and hardware profiles, never from the technology type.
* **Domain (module-internal):**
  * **`TechnologyType`** (lookup): `Id` (`int`), `InternalCode` (`bess`, `solar_pv`, `wind`, `evse`), `DisplayName`. Technology only — external protocol codes live in `ProtocolMapping`.
  * **`ProtocolMapping`** (lookup): `Id` (`long`), `TechnologyTypeId`, `ProtocolStandard` (e.g. `OpenADR_3.0`, `IEEE_2030_5`), `ResourceCategory` (e.g. `Storage`), `AssetTypeCode` (e.g. `Battery`), `ExtraAttributes` (JSON). One technology type can have one row per external standard it speaks. Keeps protocol-to-internal mappings in data rather than hardcoded in application code.
  * **`InterconnectionType`** (lookup): `Id` (`int`), `InternalCode` (`btm`, `ftm`, `atm`), `DisplayName`. Meter position only. Voltage level comes from the connection node, islanding is premise/microgrid capability plus runtime state, and virtual metering belongs to program enrollment (later phase).
  * **`CustomerSegment`** (lookup): `Id` (`int`), `InternalCode` (`residential`, `commercial_industrial`), `DisplayName`.
  * **`HardwareProfile`:** `Id` (`int`), `Code` (e.g. `tesla_powerwall_3`), `Manufacturer`, `Model`, `TechnologyTypeId` (null = conversion/control equipment such as inverters, gateways, plant controllers), per-unit `RatedKw` / `RatedKva` / `RatedKwh`, `RoundTripEfficiency`. The manufacturer of an asset's control profile selects its device-integration adapter.
  * **Hierarchy:** `Substation` (`RatedCapacityKva`) → `Feeder` (`NominalVoltageKv`, optional `RatedCapacityKva`) → `NeighborhoodTransformer` (`RatedCapacityKva`, `SecondaryVoltageV`) → `Premise` (or `Feeder` → `Premise` for primary service). Each has `Id` (`long`), `Code`, a typed parent ID, and PostGIS location/geometry. Node ratings are required: unknown capacity must never mean "unconstrained." Parent IDs record the normal configuration, not the energized path.
  * **Grid node identity:** the four levels and `Microgrid` also carry a `GridNodeId` (`long`) from `topology.grid_nodes`, a registry with one id space across levels. A trigger assigns it on insert and it never changes; a registry row outlives its node, so node-keyed data keeps a stable reference. They may inherit a module-internal abstract `GridNode` base class (`Id`, `GridNodeId`, `Code`, `Location`) for level-agnostic code. It is **not** mapped as EF Core inheritance: each level keeps its own table and `IDENTITY` primary key, and `GridNodeId` is database-generated (never written by EF). `GridNodeId` is the reference other modules use for node-keyed data.
  * **`Premise`:** `CustomerSegmentId`, `ServiceCapacityKva` (import limit), `ExportLimitKw` (site interconnection limit; `0` = non-export), `CanIsland`.
  * **`Microgrid`:** `Id` (`long`), `Code`, connection at exactly one of `FeederId` / `SubstationId`, `CanIsland`, `ImportLimitKw`, `ExportLimitKw`.
  * **`Asset`** (aggregate root, owns its components): `Id` (`long`), `Code`, connection at exactly one of `PremiseId` / `NeighborhoodTransformerId` / `FeederId` / `SubstationId`, `MicrogridId` (`long?`), `InterconnectionTypeId`, `ControlHardwareProfileId`, `RatedPMinKw` / `RatedPMaxKw` (negative = charging/import), `RatedKva`, `ExportLimitKw` (per system), `LifecycleState` (planned → installed → commissioned → decommissioned, with field dates `InServiceDate` / `DecommissionedDate`), and `ParticipationState` (not enrolled, active, suspended, opted out). Makeup (PV only, storage, PV + storage) and energy capacity are derived from its components. Decommissioning is a lifecycle change that keeps the asset; deleting is only for correcting a record entered in error.
  * **Grid state:** connectivity and energization (connected and energized, connected and de-energized, islanded, disconnected) are append-only events on grid nodes and assets, with field and recorded times. GridTopology derives each asset's effective state from its path; dispatch eligibility is commissioned, active, energized, and available (availability from telemetry).
  * **`AssetComponent`:** `TechnologyTypeId`, `HardwareProfileId`, `UnitCount`, installed `RatedKw` (PV kW-dc or battery power) and `RatedKwh`. Never dispatched directly.
* **Invariants enforced in the entities** (they need cross-table checks the database cannot express):
  * Transformer-, feeder-, or substation-connected assets are `ftm`.
  * An asset has at least one component.
  * Microgrid members sit under the microgrid's own feeder (or substation).
  * A component's technology matches its hardware profile (the database also enforces this with a composite foreign key).
* **Change tracking (system-versioned):** every topology table has `created_at` and `sys_period`, a `<table>_history` twin, a versioning trigger, and a `<table>_as_of(timestamptz)` function. `lower(sys_period)` doubles as the last-updated time. The init script verifies that every history table matches its main table.
  * **Persistence:** EF Core maps the main tables as usual. `created_at` and `sys_period` are database-generated (never written by EF), and history tables are not mapped as entities.
  * **Who and why:** a `Persistence/` interceptor runs `SET LOCAL rva.actor`, `rva.actor_type`, and `rva.change_reason` at the start of each command's transaction, so every topology change runs in an explicit transaction.
  * **As-of reads** use `FromSql` against the `_as_of` functions.
  * **Outbox:** integration events are written to `topology.outbox_messages` in the same transaction as the change, and delivered by a background publisher.
* **Schema (`topology`):** as defined in `rva_core_init.sql`. Key database-level rules:
  * single-connection `CHECK`s on assets (`num_nonnulls(premise_id, neighborhood_transformer_id, feeder_id, substation_id) = 1`), premises (`neighborhood_transformer_id` / `feeder_id`), and microgrids (`feeder_id` / `substation_id`)
  * `grid_node_id` on each node table is `UNIQUE`, assigned by trigger, and immutable; `grid_nodes` rejects `UPDATE` and `DELETE`
  * asset power within kVA (`GREATEST(ABS(rated_p_min_kw), ABS(rated_p_max_kw)) <= rated_kva`)
  * composite FK `asset_components (hardware_profile_id, technology_type_id)` → `hardware_profiles`, so a component can only use an energy product of its own technology
  * `asset_components.asset_id` cascades on delete; all other topology FKs restrict
* **Seeding:** Idempotent seed of lookups (including protocol mappings) and hardware profiles, keyed on natural codes (`ON CONFLICT (…_code) DO NOTHING`), plus a sample Portland topology for development. The four reference scenarios are **test fixtures**, seeded into a test database, never mixed into the development topology:
  * **A:** home with a DC-coupled Powerwall 3 + PV (one asset, two components, inverter-capped rating).
  * **B:** non-export C&I warehouse with separate PV and battery systems (`premises.export_limit_kw = 0`, behind-the-meter microgrid via `can_island`).
  * **C:** residential home with three Powerwall 3s and a 10 kW site export cap (large equipment, still `residential`).
  * **D:** community microgrid on a feeder — 20 homes (Tesla DC-coupled, Enphase single-gateway, PV-only, and a mixed-vendor home with two assets), a school, and a feeder-connected community battery — including a neighborhood transformer whose connected inverter capacity exceeds its rating.
* **Features:** `RegisterAsset`, `ChangeAssetLifecycle` (install, commission, decommission), `ChangeParticipation`, `RecordGridStateEvent`, `GetAsset`, `GetSubstationTree`, `GetTopologySnapshot(scopeRoot, detail, asOf)` (bulk export of a subtree at a chosen level of detail and topology version, used by `Rva.Simulator`).
* **Contracts:** `IGridTopologyQueries` (`GetAsset(id, asOf?)`, `GetAssetsBySubstation`, `GetConstraintPath(assetId, asOf?)`, `GetGridState(assetId, asOf?)`, `GetTopologySnapshot(scopeRoot, detail, asOf)`, `ResolveTechnologyType`, `GetProtocolMappings`), `AssetSnapshot` (connection point and substation, ratings, site and microgrid limits, makeup, control manufacturer, lifecycle and participation state, `ValidFrom`), `ProtocolMappingSnapshot`, and the events `AssetRegistered`, `AssetUpdated`, `AssetDecommissioned`.
  * `GetConstraintPath` returns the nodes whose limits apply to an asset, from its connection point up to the substation, each with its `GridNodeId`, level, code, and ratings, plus the site and microgrid limits. Phase 1 derives it from the hierarchy; a later network model can derive it from the energized path. Constraint and dispatch code uses it instead of walking parent IDs.

---

## Step 2: Simulation Module (`Rva.Modules.Simulation`)

**Goal:** Own the simulation run ledger and per-run asset overrides. This module does **not** run the Chaos Engine — `Rva.Simulator` does (Step 4).

* **Domain:** `SimulationRun` (lifecycle `Pending` → `Running` → `Completed` | `Failed`, with transitions enforced by the entity and backed by a database trigger; scope fixed at creation, window and configuration fixed once it leaves `Pending`; can be scoped to a microgrid; a `RunKind`, `chaos` in Phase 1), `SimulationAssetState` (per-run override of an asset's starting availability, power bounds, and state of charge).
* **Change tracking (append-only events):** simulation data is things that happen, so it is recorded as events rather than versioned:
  * every status a run enters is written to `simulation_run_events` by trigger, with the reason and actor from `SET LOCAL`; the table rejects updates and deletes, and a run with events cannot be deleted
  * run inputs (scope, `topology_as_of`, overrides) are fixed once a run leaves `Pending`, enforced in the database, so a run can be replayed exactly
* **Scope:** each run records which part of the grid it covers (`scope_level_id` + `scope_root_code`, e.g. `neighborhood_transformer` / `XFMR-07-A`), how much detail it loads (`detail_level_id`: hierarchy, assets, or assets with components), and which topology version it uses (`topology_as_of`, defaulting to the run's creation time).
* **Schema (`simulation`):** as defined in `rva_core_init.sql`:
  * `simulation_runs`: `run_kind_id`, `created_at`, `started_at` (NULL while Pending), `ended_at`, `updated_at`, optional `simulated_window` (simulated time, independent of wall-clock execution), optional `initial_state_as_of` (start from measured asset state at that moment instead of a synthetic start; used by later twin run kinds), the scope columns above, `configuration_json` (refers to assets by `asset_code`). Run kind, `topology_as_of`, and `initial_state_as_of` are fixed at creation.
  * `run_kinds`: `chaos` only in Phase 1; `forecast`, `what_if`, and `replay` are added as rows when a twin phase needs them
  * `simulation_run_events`: append-only lifecycle history
  * `simulation_asset_states`: per-run overrides of each asset's **starting state** (one per asset), `asset_id` with **no FK** (GridTopology owns assets), changeable only while the run is Pending. Changes during a run (faults, switching, load steps) go in `configuration_json` or a future per-run event table, never here
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
  * `asset_telemetry` hypertable partitioned by `timestamp`: `timestamp` TIMESTAMPTZ NOT NULL, `asset_id` BIGINT NOT NULL, `simulation_run_id` UUID NULL, `active_power_kw`, `reactive_power_kvar`, `state_of_charge_pct`, `voltage_v` (DOUBLE PRECISION), `quality_type_id` INTEGER NOT NULL (good, suspect, or estimated) and `source_type_id` INTEGER NOT NULL (device, manufacturer cloud, or simulator), each referencing a lookup table in `rva_telemetry`. No FKs to other databases.
  * **Twin readiness:** measurements taken at grid nodes rather than devices (SCADA at the substation and feeder head, smart meters at premises) are a future `node_telemetry` hypertable keyed by `grid_node_id`, with the same `quality_type_id` and `source_type_id` columns. Phase 1 does not build it, but `asset_telemetry` uses the same column conventions so the two can be queried together.
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
* [ ] `docker compose up` starts both databases, Mosquitto, and Redis, and runs the init scripts cleanly; `infrastructure/postgres/tests/run_tests.sh` passes.
* [ ] Seed scripts can be re-run without duplicating rows (natural-code idempotency), and the reference scenarios A–D load with all database constraints passing.
* [ ] GridTopology serves the topology and publishes asset events; Telemetry's `known_assets` stays in sync.
* [ ] Simulation runs move through their lifecycle via the API, with overrides validated against GridTopology.
* [ ] `Rva.Simulator` drives 50,000 nodes and its CloudEvents telemetry lands in `asset_telemetry` through the ACL.

---

## Deferred (recorded, not in Phase 1)

* **What-if overlays:** generalize `simulation_asset_states` into per-run overlays that add, remove, or modify assets and limits on top of the base topology. Phase 1 uses only per-asset overrides.
* **Digital twin:** the electrical network model (buses, line segments with impedance, switches with normal and current state, phase), node measurements, a power-flow evaluator behind `OperatingEnvelope`, and forecast/what-if runs seeded from live state. Phase 1 only prepares the structure for it (see Cross-cutting decisions).
* **Premise load:** a load profile reference and peak kW on premises, so transformer loading reflects net load (consumption minus DER output). Phase 1 compares inverter capacity with node ratings.
* **Manufacturer cloud adapters:** device integration beyond MQTT; `hardware_profiles.manufacturer` and `known_assets.control_manufacturer` are already in place to route them.
* **Audit module timing and dispatch phases:** scheduling the Audit module before the first grid-impacting command, and adding Portfolio/Enrollment and Dispatch phases, is a separate roadmap change to `MASTER_PLAN.md`.
