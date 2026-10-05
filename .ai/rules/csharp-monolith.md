# C# Modular Monolith & Architecture Rules (`src/Rva.Backend`)

## 1. Sovereignty Principle
The C# core is the absolute **source of truth** for grid state, security, and dispatch. It never blocks on or blindly trusts the Python AI sidecar (`Rva.Intelligence`). Sidecar output is strictly a recommendation, wrapped in a **Polly Circuit Breaker**, featuring a "Safe-Fail" default if the sidecar is unhealthy or tripped.

## 2. Solution Layout
The backend is a **Modular Monolith** built from **separate C# projects (`.csproj`)**, not subfolders, so module boundaries are enforced at compile time. `Rva.Host` is the only host; modules are class libraries it composes.

```text
src/
├── Rva.Backend/
│   ├── Rva.Host/                               <-- Host & composition root (DI, Serilog, middleware, endpoints, background workers)
│   ├── Rva.SharedKernel/                       <-- Shared vocabulary: typed IDs, units, reference codes, Result
│   └── Modules/
│       ├── Rva.Modules.GridTopology/           <-- Module implementation (internal)
│       ├── Rva.Modules.GridTopology.Contracts/ <-- Module's public API: query interfaces, integration events, snapshots
│       ├── Rva.Modules.Telemetry/
│       ├── Rva.Modules.Telemetry.Contracts/
│       ├── Rva.Modules.Simulation/
│       └── Rva.Modules.Simulation.Contracts/
├── Rva.Simulator/                              <-- Separate process: Chaos Engine / device fleet (NOT a module)
└── Rva.Intelligence/                           <-- Separate process: Python AI sidecar (NOT a module)
tests/
└── Rva.ArchitectureTests/
```

**Allowed project references** (anything not listed is prohibited):

| Project | May reference |
|---|---|
| `Rva.Host` | All modules and their `.Contracts`, `Rva.SharedKernel` |
| `Rva.Modules.<X>` | `Rva.SharedKernel`, its own `.Contracts`, other modules' `.Contracts` |
| `Rva.Modules.<X>.Contracts` | `Rva.SharedKernel` only |
| `Rva.SharedKernel` | Nothing outside the .NET base class library |

`Rva.Simulator` and `Rva.Intelligence` never reference backend projects or databases. They talk to the backend only over the network — HTTP via `Rva.Host` and MQTT.

## 3. Module Ownership
* **Each module owns its domain model.** Entities, aggregates, and invariants live inside the module that owns them (e.g. `Asset` and the grid hierarchy live in GridTopology). Other modules never use another module's entities.
* **Each module owns its data.** One `DbContext` per module, mapped to that module's own schema or database. No module queries another module's tables or `DbContext`, and there are **no foreign keys across module boundaries** — cross-module references are plain IDs (e.g. `asset_id BIGINT` with no FK).
* **Communication goes through `.Contracts` only:**
  * **Synchronous queries** — interfaces such as `IGridTopologyQueries`, returning immutable snapshot records. These are in-process calls; prefer them for reads that must be current.
  * **Integration events** — records such as `AssetRegistered`, for modules that keep a local read model.
* **Safety-critical reads are never served from a cached copy.** Anything that bounds a grid-impacting command (asset power limits, substation capacity) must be read through the owning module's query interface. Local read models are for high-volume, staleness-tolerant paths such as telemetry ingestion.
* **Internal by default.** Everything in a module implementation project is `internal` except its DI registration extension (e.g. `AddGridTopologyModule()`) and endpoint mapping.

## 4. Vertical Slicing within Modules
Inside a module, code is organized by feature, not by technical layer. Do **not** create `Controllers/`, `Services/`, or `Repositories/` folders.

```text
Rva.Modules.<Area>/
├── Features/
│   └── <FeatureName>/      <-- Endpoint, command/query, handler, validator, request/response DTOs
├── Domain/                 <-- Module-internal entities and value objects shared by this module's features
├── Persistence/            <-- The module's DbContext and EF Core Fluent API configurations
└── <Area>Module.cs         <-- Public DI registration + endpoint mapping
```

* **DTOs are feature-local.** Request/response types live in the feature folder that uses them. They never go in `Rva.SharedKernel` or another module.
* **Fluent API only.** Map entities with EF Core Fluent API configurations in `Persistence/`; do not put ORM attributes (`[Table]`, `[Column]`, `[Key]`, `[ForeignKey]`) on domain types.

## 5. The Shared Kernel (`Rva.SharedKernel`)
**Grow it on demand:** a type is added only when a second module needs it — never speculatively. A type that one module uses stays in that module.

A type belongs in the kernel only if **all** of these hold: it is used by two or more modules, has no identity or lifecycle, is immutable, rarely changes, and depends on nothing but the .NET base class library.

* **Contents** (what it may hold, added as needed):
  * Typed identifiers — `AssetId`, `SubstationId`, `FeederId`, `TransformerId`, `PremiseId`, `MicrogridId`, `SimulationRunId`.
  * Units — `Kilowatts`, `KilowattHours`, `KilovoltAmperes`, `Kilovars`, `Volts`, `Percentage`.
  * Reference codes — `TechnologyTypeCode`, `InterconnectionTypeCode`, `ProtocolStandard`. These wrap the lookup-table code string; they are **not** C# enums, so adding a lookup row never requires a code change. The owning module validates codes against its table.
  * `Result`, `Result<T>`, `Error`, and the `IIntegrationEvent` marker.
* **Prohibited:** entities, business logic, DTOs, module contracts, `DbContext`s, EF Core or any other package dependency, and operational workflows.

## 6. Cross-Cutting Concerns
* **Errors:** Expected failures return `Result`/`Result<T>`; exceptions are for unexpected faults only.
* **Logging:** Serilog is configured only in `Rva.Host`. Modules inject `ILogger<T>` and never reference Serilog directly. The audit pipeline is covered in `.ai/rules/security.md`.

## 7. High-Throughput Ingestion & Anti-Corruption Layer (ACL)
* **Asynchronous Pipelines:** MQTT ingestion uses `System.Threading.Channels` (Producer/Consumer) inside `Rva.Modules.Telemetry` to prevent bottlenecks.
* **Anti-Corruption Layer:** External payloads (CloudEvents envelopes carrying OpenADR / IEEE 2030.5 identifiers) pass through the ACL before touching the domain model or the TimescaleDB hypertable. The ACL resolves device identifiers against its local read model of known assets (fed by GridTopology integration events) and resolves protocol classifications through GridTopology's `protocol_mappings` contract (`ResolveTechnologyType` / `GetProtocolMappings`) — never by reading GridTopology's tables.
* **Data-driven, cached lookups:** Mappings live in lookup tables, not in hardcoded `switch` statements. Load them into memory at startup and refresh on change — never query the database per message.

## 8. Architectural Fitness Enforcement
Every structural rule above is backed by architecture tests in `tests/Rva.ArchitectureTests`, which fail the build on violation. Project references are checked in the `.csproj` files, so a forbidden reference fails as soon as it is added; type rules use ArchUnitNET and reflection on the compiled assemblies, selecting types **by assembly**, not by exact namespace (vertical slices put most types in sub-namespaces). The test project must reference every backend project, and a test enforces this. The suite must cover:
* No module implementation project depends on another module's implementation project (all pairs).
* `.Contracts` projects depend only on `Rva.SharedKernel`.
* `Rva.SharedKernel` depends only on `System.*`.
* No type outside a module references that module's `DbContext`.
* No ORM attributes on any domain type.
* Only `<Area>Module` is public in a module implementation project.
* No `Controllers/`, `Services/`, or `Repositories/` folders or namespaces in modules.

These tests run on every pull request.

## 9. Persistence & Change Tracking
Every module chooses its change-tracking model by the nature of its data:

* **State that gets edited is system-versioned** (e.g. topology: assets, ratings, limits, lookups). Each table carries `created_at` and a `sys_period` validity range, and has a `<table>_history` twin, a versioning trigger, and a `<table>_as_of(timestamptz)` function. Version **all** tables a snapshot joins across, or reconstructed history is inconsistent. The history table's columns must match the main table's (same order) plus `operation`, `changed_by`, `actor_type`, `change_reason`; the init script fails if they do not. EF Core maps only the main tables and never writes `created_at` or `sys_period`.
* **Things that happen are append-only events** (e.g. simulation run lifecycle, telemetry, later dispatch commands and audit). Event tables reject `UPDATE` and `DELETE` in the database, not just by convention. Inputs to an execution (e.g. a run's scope, topology version, and overrides) are immutable once it starts.
* **Who and why** are recorded from per-transaction settings, set by the module at the start of each command's transaction: `SET LOCAL rva.actor = '<uuid>'`, `rva.actor_type` (`user` | `service` | `system`), and `rva.change_reason`. Every change therefore runs in an explicit transaction.
* **Identities:** user and service IDs are UUIDv7, owned by the Identity module (Phase 3). A login provider's `(issuer, subject)` is mapped to a user ID and is never used as a key. Actor IDs and `actor_type` are stored as plain values with no cross-module foreign key. Audit records copy the actor's details at the time of writing.
* **Outbox:** each module writes its integration events to its own `outbox_messages` table in the same transaction as the change, and a background publisher delivers them. Versioning provides history; the outbox provides reliable delivery.
* **Timestamps:** use `clock_timestamp()`, not `now()`, so several changes in one transaction stay ordered. Every table that stores entities has `created_at`; versioned tables use `lower(sys_period)` as their last-updated time instead of a separate `updated_at`.
