# Plan p1-s01: GridTopology module

**Step:** `p1-s01` (Phase 1 Step 1 in `PHASE_1_PLAN.md`) · **Status:** approved, in progress; part statuses are tracked in the Parts table below · **Depends on:** `p1-s00a`–`p1-s00f` (done; `s00c` merged in PR #14)

The domain model, invariants, feature list, and contract list are in `PHASE_1_PLAN.md` Step 1, and the schema is `infrastructure/postgres/core/rva_core_init.sql`. This plan does not restate them. It sets the module's internal structure, splits the step into parts that can each be reviewed on their own, defines how errors are handled, and lists the tests each part must pass.

## 1. Scope

**In scope:** the GridTopology module and its contracts; the shared kernel types those contracts need; the catalog and development seed; the outbox and relay; a readiness check; the module's test projects.

**Out of scope (later steps or phases):**
- Create and update endpoints for hierarchy nodes. In Phase 1 the hierarchy comes from the seed.
- Editing an asset's ratings or components, or moving it to another connection point. Reparenting (GT-09) is done in SQL in Phase 1.
- Deleting an asset to correct a record entered in error. The database supports it (with a change reason); the feature is deferred.
- Spatial queries (within a radius, inside a polygon). Phase 1 stores and returns locations only.
- Authentication and RBAC (Phase 2 service JWT, Phase 3 OIDC), recorded as risk R1.
- Multi-program enrollment (GT-16), real-time availability (telemetry, Step 3), the Telemetry `known_assets` consumer (Step 3), and Simulation's use of the contracts (Step 2).
- DDL changes.

## 2. Decisions

Decided 2026-10-30. D1–D4 close the open items in `.ai/rules/testing.md` section 4, and `p1-s01a` records them there.

| # | Decision | Decided |
|---|---|---|
| D1 | Database integration tests from .NET | `Testcontainers.PostgreSql`, running the test image built from `infrastructure/postgres/tests/Dockerfile` (PostGIS, pgTAP, psql), with `rva_core_init.sql` mounted as in compose. The first commit of `s01a` adds it to the approved table in `testing.md` and to `TestFrameworkRules.ApprovedTestPackages`; `TestFrameworkTests` fails until both are done. |
| D2 | Test doubles | Hand-written fakes (for example `IGridTopologyQueries`, `IIntegrationEventPublisher`, `TimeProvider`). No mocking library. |
| D3 | Test project layout | Two projects per module: `tests/Rva.Modules.GridTopology.Tests` (unit, no Docker) and `tests/Rva.Modules.GridTopology.Integration.Tests` (real Postgres, host in memory). |
| D4 | Assertion library | xUnit `Assert` only. |
| D5 | Runtime packages | `Npgsql.EntityFrameworkCore.PostgreSQL` and `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`, pinned in `Directory.Packages.props`. Column names are mapped explicitly with the Fluent API; keys are `{entity}_id` and codes `{entity}_code`, so a naming-convention package would need overrides anyway. |
| D6 | Schema ownership | No EF migrations. `rva_core_init.sql` stays the source of truth and EF maps onto it. A schema-mapping test (A-03) catches drift. |
| D7 | Lifecycle transitions | Forward only: planned → installed → commissioned → decommissioned. Skipping forward is allowed (planned → commissioned). Decommission is allowed from installed or commissioned. A planned asset that is never built is a correction (deferred delete). |
| D8 | Participation transitions | not_enrolled → active; active ↔ suspended (suspending needs a reason); active or suspended → opted_out; opted_out → active. Active and suspended need a commissioned asset. Decommissioning needs not_enrolled or opted_out (GT-16). |
| D9 | Repeating a command already in effect | No-op success: 200, no history row, no event, so retries are safe. An identical re-registration (same code, same body) is also a 200 no-op; a different body for an existing code is 409. |
| D10 | Grid states allowed on an asset-targeted event | `disconnected` and `connected_energized` (reconnect) only. Islanded and de-energized come from the path, as in the `pg_temp.asset_grid_states` reference. |
| D11 | Grid state events need a reason | Yes. An outage record without a cause is weak evidence for NERC CIP. |
| D12 | Seed mechanism | Idempotent SQL in `infrastructure/postgres/seed/`: `catalog.sql` (protocol mappings, hardware profiles) and `dev_topology.sql` (sample Portland topology), applied by `seed.sh`. The host never seeds at startup, and tests never load the dev seed. |
| D13 | Outbox delivery | `published_at` means "handed to the in-process dispatcher": at-least-once up to the dispatcher. The relay does not retry a handler failure after that (the `s00b` dispatcher is best-effort). A row that fails 10 times is parked (skipped, logged at Error once). Published rows are deleted after 7 days. Recorded as risk R2. |
| D14 | Order relative to `p1-s00c` | Done: `s00c` merged first (PR #14), so the `ci` workflow checks every GridTopology PR. `s01a` extends its build-test job to run the integration tests (Docker is available on `ubuntu-latest`). |
| D15 | Snapshot content and detail vocabulary | The GridTopology contract defines its own detail levels (`nodes`, `assets`, `components`). It cannot use `simulation.detail_levels`, which another module owns; Simulation maps its value. The snapshot holds commissioned assets only (matching `pg_temp.scope_assets`), each with its effective grid state at `asOf`. |
| D16 | Startup and health | The host must start without a database (the `ci` image job and `Rva.Host.Tests` do this). The module never blocks startup on the database: lookups load on first use, and the relay backs off while the database is unavailable. The module registers a readiness check (`rva_core` reachable, every known lookup code present) tagged `ready`; the host maps `/health/ready` for that tag, and `/health` stays liveness-only as `s00c` decided. |
| D17 | Actor in Phase 1 | A fixed system actor, a constant UUIDv7 in the module, with `actor_type = system`. Each command supplies its own change reason. Phase 3 replaces it with the authenticated user or service (R1). |
| D18 | Utility time zone | `GridTopology:UtilityTimeZone` (IANA id, `America/Los_Angeles` in `appsettings`), validated at startup like the deployment options. "Not in the future" checks on in-service and decommission dates use the local date in that zone. It moves to `Rva:Deployment` when a second module needs it. |
| D19 | Device key and event payloads | `asset_code` is the device key the simulator publishes on MQTT until manufacturer adapters arrive. Events carry `AssetId`, `asset_code`, technology codes, ratings, lifecycle and participation state, and are typed `<Name>.v1`. A breaking change adds a new version, and the relay handles both until the old rows are gone. |
| D20 | Missing limits | Premise and microgrid limits that are null mean "no limit at this level", which is different from 0. The contract says so, and tests check the difference (C-06c). |
| D21 | Control profile | Optional in Phase 1 (devices integrate over MQTT only). An asset without one has `ControlManufacturer = null`. |
| D22 | Lookup refresh | Lookups load once and are kept for the life of the process; adding a lookup row needs a restart in Phase 1. This is a deliberate exception to "refresh on change" in `csharp-monolith.md` section 7. |

**Schema questions:**
- **Q1 (decided: keep nullable):** `feeders.rated_capacity_kva` is nullable, while `PHASE_1_PLAN.md` says unknown capacity must never mean "unconstrained". The feeder rating stays optional, as in the phase plan's model. `GetConstraintPath` and the snapshot report an unrated feeder explicitly (`RatedCapacityKva = null`, `IsRated = false`), never as an unbounded value (C-12).
- **Q2:** the domain checks that `occurred_at` is not in the future with the app clock; the database checks against `clock_timestamp()`. A `ck_grid_state_events_not_future` violation maps to the same 400 as the domain check (D-21), so clock skew is an expected failure, not a 500.

## 3. Project structure

### Module (`src/Rva.Backend/Modules/Rva.Modules.GridTopology/`)

```text
Rva.Modules.GridTopology/
├── GridTopologyModule.cs              the only public type: AddGridTopologyModule / MapGridTopologyEndpoints
├── GridTopologyQueries.cs             implements IGridTopologyQueries; each method delegates to its feature's handler
├── Features/
│   ├── RegisterAsset/                 Endpoint, Request, Response, Handler (one folder per feature, same shape)
│   ├── ChangeAssetLifecycle/
│   ├── ChangeParticipation/
│   ├── RecordGridStateEvent/
│   ├── GetAsset/
│   ├── GetAssetsBySubstation/         contract only, no endpoint
│   ├── GetSubstationTree/
│   ├── GetConstraintPath/             contract only
│   ├── GetGridState/                  contract only
│   ├── ResolveNode/                   contract only
│   ├── GetTopologySnapshot/
│   └── ProtocolMappings/              ResolveTechnologyType, GetProtocolMappings (contract only)
├── Domain/
│   ├── Assets/                        Asset (aggregate root), AssetComponent, AssetLifecycle, Participation, AssetErrors
│   ├── Hierarchy/                     GridNode (abstract, not EF inheritance), Substation, Feeder,
│   │                                  NeighborhoodTransformer, Premise, Microgrid
│   ├── Catalog/                       TechnologyType, ProtocolMapping, InterconnectionType, CustomerSegment, HardwareProfile
│   ├── GridState/                     GridStateEvent, EffectiveGridState (pure derivation)
│   └── ConstraintPaths/               ConstraintPath, ConstraintPathBuilder (pure; an incomplete path is an error)
├── Persistence/
│   ├── GridTopologyDbContext.cs       internal; schema "topology"; xmin as the concurrency token
│   ├── Configurations/                one Fluent API configuration per entity (no ORM attributes)
│   ├── UnitOfWork.cs                  opens the transaction and applies the change context; the only path to SaveChanges
│   ├── ChangeContextInterceptor.cs    SET LOCAL rva.actor / rva.actor_type / rva.change_reason at transaction start
│   ├── AsOfReads.cs                   FromSql over the <table>_as_of functions
│   ├── DatabaseErrorClassifier.cs     PostgresException SQLSTATE/constraint → expected Error or unexpected fault
│   ├── LookupCatalog.cs               lookup ids by code, loaded on first use (D16, D22)
│   ├── ReadinessCheck.cs              health check tagged "ready" (D16)
│   └── Outbox/                        OutboxMessage, OutboxWriter, OutboxRelay (BackgroundService), EventSerializer
└── Http/
    └── ResultHttpMapping.cs           Result/Error → TypedResults + problem+json (module-local; Host cannot be referenced)
```

- Handlers are called directly from endpoints. No mediator library.
- The module declares `<InternalsVisibleTo>` for its two test projects. The architecture rule "only `<Area>Module` is public" is unaffected.
- Routes are grouped under `/api/topology`: `POST /assets`, `GET /assets/{code}?asOf=`, `POST /assets/{code}/lifecycle`, `POST /assets/{code}/participation`, `POST /grid-state-events`, `GET /substations/{code}/tree?asOf=`, `GET /snapshot?level=&code=&detail=&asOf=`. External callers use natural codes. Node codes are unique only within a level, so anything that takes a node code also takes its level.
- Locations are returned as GeoJSON (SRID 4326): points for substations, transformers, and premises, and a LineString for feeders.

### Contracts (`Rva.Modules.GridTopology.Contracts/`)

```text
├── IGridTopologyQueries.cs            every method returns Task<Result<T>> and takes a CancellationToken
├── Snapshots/                         AssetSnapshot, ConstraintPathSnapshot, ConstraintNodeSnapshot, GridStateSnapshot,
│                                      GridNodeRef, TopologySnapshot, ProtocolMappingSnapshot, SnapshotDetail,
│                                      GeoPoint, GeoLine
├── Events/                            AssetRegistered, AssetUpdated, AssetDecommissioned (IIntegrationEvent)
└── GridTopologyErrors.cs              public error codes callers can match on
```

`IGridTopologyQueries` is the Step 1 list plus `ResolveNode(level, code, asOf?)`, which Simulation needs to validate a scope root (SIM-01) without pulling a whole snapshot. Every read takes `asOf?`, including `GetAssetsBySubstation`, which pages with a cursor.

The main snapshot records:
- **`AssetSnapshot`:** id, code; connection level, node code and `GridNodeId`; substation id and code; microgrid code (if any); interconnection type; `RatedPMinKw`, `RatedPMaxKw`, `RatedKva`, asset export limit; site limits (premise service capacity and export limit); microgrid import and export limits; makeup (PV, storage, PV + storage, other) and total energy capacity from the components; control manufacturer (nullable, D21); lifecycle and participation state; `ValidFrom`.
- **`ConstraintPathSnapshot`:** the ordered nodes (each `GridNodeId`, level, code, `RatedCapacityKva`, `IsRated`); site and microgrid limits; `EffectiveExportLimitKw` (minimum of asset `p_max`, asset export limit, premise export limit, microgrid export limit) and `EffectiveImportLimitKw` (minimum of asset `|p_min|`, premise service capacity, microgrid import limit). Null limits follow D20. Shared node capacity is not folded into these per-asset limits; dispatch allocates it.
- **`GridStateSnapshot`:** effective grid state, the node or asset that caused it, `IsRegistryEligible` (commissioned, active, energized; availability comes from telemetry).

`GeoPoint` and `GeoLine` are plain records here, not NetTopologySuite types, because Contracts may reference only the shared kernel. Lifecycle, participation, and grid states are string codes, not enums, following the reference-code rule.

As built in `p1-s01c` (the final contract is in `docs/architecture/module-contracts.md`):
- **Site and microgrid limits** are grouped as `SiteSnapshot` (premise ref, customer segment, service capacity, export limit, `CanIsland`) and `MicrogridSnapshot` (microgrid ref, import and export limits, `CanIsland`). Both `AssetSnapshot` and `ConstraintPathSnapshot` carry them, and each is null when it does not apply.
- **Makeup** is one of the `AssetMakeups` codes. It is null when an asset has no components recorded; `EnergyCapacityKwh` is null when no component records energy.
- **`GridNodeLevels`** holds the level codes.
- **`GetAssetsBySubstation`** takes a substation code and returns an `AssetPage`. Its cursor pins the topology version, to the database clock when no `asOf` is given.
- **Constraint path nodes** run from the connection node up to the substation, and a premise's rating is its service capacity. The microgrid is not a node on the path; it is carried as `Microgrid`. The effective limits are non-negative magnitudes.
- **`asOf` in the future** is checked against the database clock as well as the app clock, so a time that came from the database clock is never refused because of skew (Q2).
- **Current reads** (`asOf` null) run in one REPEATABLE READ transaction, so a commit landing between their statements cannot mix versions.

### Shared kernel additions (`Rva.SharedKernel/`)

The kernel normally grows only when a second module needs a type (`csharp-monolith.md` section 5). Contract signatures can only use kernel types, so this step adds the ones the GridTopology contracts use, and Simulation and Telemetry will reuse them: `Result`, `Result<T>`, `Error` (`Code`, `Message`, `Kind`); `AssetId`, `GridNodeId`, `SubstationId`, `FeederId`, `TransformerId`, `PremiseId`, `MicrogridId`; `Kilowatts`, `KilowattHours`, `KilovoltAmperes`; `TechnologyTypeCode`, `InterconnectionTypeCode`, `ProtocolStandard`. Units reject NaN and infinity. `Volts` is not added; the transformer secondary voltage stays a `double` in the snapshot.

### Tests

```text
tests/Rva.Modules.GridTopology.Tests/                  unit: domain rules, derivations, Result mapping (no Docker)
tests/Rva.Modules.GridTopology.Integration.Tests/
├── Infrastructure/   PostgresContainerFixture (one container per run), TemplateDatabase (init applied once;
│                     CREATE DATABASE … TEMPLATE per test class), FixtureLoader, TopologyHostFactory
│                     (WebApplicationFactory<Program>, Core connection string → the test database; Production
│                     environment for the sanitization tests), FaultInjection/
├── Persistence/      schema mapping, change context, concurrency, error classification
├── Features/         one test class per feature (handler/contract level and endpoint level)
├── Parity/           C# results vs the pgTAP reference queries (pg_temp.asset_paths, asset_grid_states, dispatch_eligible)
└── Outbox/
```

Fixtures A–D depend on the `pg_temp` helpers in `_helpers.sql`, which also create the pgTAP extension and set transaction-local `rva.*` settings. `FixtureLoader` therefore runs `_helpers.sql` and the fixture with psql inside the test image, in one committed transaction, into the template database. The SQL fixtures stay the single source for both suites.

## 4. Error-handling contract

**Expected failures** return `Result` failures with an `Error`; they are never thrown.

| `Error.Kind` | HTTP | Used for |
|---|---|---|
| Validation | 400 | request shape, ranges, missing fields, future times |
| NotFound | 404 | the resource in the route, or its version as of a time |
| Conflict | 409 | duplicate code, illegal state transition, concurrent change (xmin) |
| Rule | 422 | a domain invariant broken by a reference in the body (unknown connection node, non-FTM above the meter, member outside its microgrid) |

Responses are `application/problem+json` (RFC 9457) with `code` (for example `GridTopology.AssetNotFound`) and `traceId`. Expected failures are logged at Warning or below.

One exception (`p1-s01c`): `GridTopology.ConstraintPathIncomplete` means the stored topology is corrupt (a parent row is missing as of the requested time). Contract callers get it as a `Result` failure of kind Rule, so a safety-relevant read never returns a shorter path (U5). It is logged once at Error where it is found, and an endpoint answers 500 with that code rather than 422, because it is never the client's fault.

**Unexpected failures** are exceptions. In-process contract callers receive the exception; it is not wrapped in a `Result`. Over HTTP, the host's exception handler (`AddProblemDetails` and `UseExceptionHandler`, added in `s01a`) returns 500, or 503 for transient database faults. The database error classifier decides which database errors count as expected:
- `23505` on `assets.asset_code`: Conflict (two registrations raced).
- `ck_grid_state_events_not_future`: Validation (Q2).
- Any other `23xxx` or `P0001`: unexpected. The database rejected something the domain accepted, which is a validation gap.

**Every unexpected-failure test asserts:**
- **U1 Atomic:** the failed command left no row in `assets`, `asset_components`, their `_history` tables, `grid_state_events`, or `outbox_messages`.
- **U2 Sanitized:** problem+json with status 500 or 503 and a `traceId`, and no SQL, SQLSTATE, constraint name, stack trace, or connection string. Run in the Production environment; Development shows the exception page.
- **U3 Logged:** exactly one Error-level entry with the exception and `traceId`. Client cancellation logs at Information.
- **U4 Survives:** the host keeps serving, and the next request succeeds once the fault is removed. Background services catch exceptions per iteration, because since .NET 8 an unhandled `BackgroundService` exception stops the host.
- **U5 Fail-safe:** a safety-relevant read (constraint path, grid state, eligibility) returns an error. It never falls back to a value that lets dispatch do more (unconstrained, energized, eligible).

**Fault injection** (test-only, through `InternalsVisibleTo` or DI replacement in the test host):
- **FI-1:** pause or stop the container, or use an unreachable connection string.
- **FI-2:** `pg_terminate_backend` from a second connection, triggered by a test `DbCommandInterceptor` at a chosen command (for example before the outbox insert).
- **FI-3:** a row lock held on a second connection, plus a short command timeout.
- **FI-4:** a decorator that throws, around `OutboxWriter` or `IIntegrationEventPublisher`.
- **FI-5:** a direct `DbContext` write that skips domain validation, to reach the database constraints.
- **FI-6:** corrupted data in the per-test database (delete a `_history` row or a lookup row).
- **FI-7:** cancel the `CancellationTokenSource` from an interceptor at a chosen command.
- **FI-8:** a hand-written `TimeProvider` set ahead of the database clock.

## 5. Parts

Each part is one branch (`step/p1-s01x-<slug>`) and one PR. Every part's acceptance criteria also include: `dotnet build VppAggregator.slnx` and `dotnet test --solution VppAggregator.slnx` pass with no warnings; the architecture and test-guardrail suites pass; `infrastructure/postgres/tests/run_tests.sh` still passes; the `ci` workflow passes, including the image health checks; every test listed for the part in section 6 exists and passes; and the docs named in the part are updated.

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `p1-s01a` | Foundation. First commit: record D1–D4 in `testing.md` and `ApprovedTestPackages`. Then: packages (D5); shared kernel types; `GridTopologyDbContext` and mappings for every topology main table, `grid_state_events`, and `outbox_messages`; `UnitOfWork`, `ChangeContextInterceptor` with the system actor (D17), `DatabaseErrorClassifier`, `LookupCatalog`, readiness check and `/health/ready` (D16); `GridTopology:UtilityTimeZone` (D18); host problem details and exception handler; both test projects and the integration harness (container, template database, fixture loader A–D); the integration tests in the `ci` build-test job (raise its 15-minute timeout if needed) | — | Done (PR #16) | A-01..A-29 pass. `Rva.Host.Tests` and the `ci` image job pass without a database. A save outside `UnitOfWork` throws. Docs: `testing.md`, `verify.md` (the integration tests need Docker). |
| `p1-s01b` | Catalog and seed: `seed/catalog.sql`, `seed/dev_topology.sql`, `seed.sh`; `ResolveTechnologyType`, `GetProtocolMappings`. The dev topology has at least 2 substations, several feeders, and 500+ assets (the RVA target fleet) at all four connection levels, with one microgrid and one overloaded transformer, so substation-level balance can be exercised | s01a | Done (PR #19) | B-01..B-21 pass. A second run of the seed adds no rows and no history. Docs: `verify.md` (`seed.sh`). |
| `p1-s01c` | Reads: hierarchy and asset domain (read side), as-of reads, `GetAsset` (contract and endpoint), `GetAssetsBySubstation` (paged), `GetSubstationTree` (size-limited, with locations), `GetConstraintPath` with the effective export and import limits, `ResolveNode` | s01a, s01b (C-15 uses the dev seed) | Done (PR #25) | C-01..C-34 pass, including parity with `pg_temp.asset_paths` on fixtures A–D. An incomplete path returns `GridTopology.ConstraintPathIncomplete` (U5). Docs: `module-contracts.md` (final contract). |
| `p1-s01d` | Grid state: `RecordGridStateEvent` (endpoint), `GetGridState` (contract), `EffectiveGridState` derivation and registry eligibility | s01c | Not started | D-01..D-22 pass, including parity with `pg_temp.asset_grid_states` and `pg_temp.dispatch_eligible`. |
| `p1-s01e` | Asset writes: `RegisterAsset`, `ChangeAssetLifecycle` (install, commission, decommission), `ChangeParticipation`, with the Step 1 invariants and D7–D9; the event records in Contracts (D19) and `OutboxWriter`; each change writes its event to `outbox_messages` in the same transaction | s01c | Not started | E-01..E-36 pass. Every committed asset version has its outbox row (U1). |
| `p1-s01f` | Outbox relay: `OutboxRelay` (`FOR UPDATE SKIP LOCKED`, ordered by `occurred_at`, backoff, parking, retention per D13), event deserialization by versioned type | s01e | Not started | F-01..F-25 pass. An exception in the relay never stops the host (U4). |
| `p1-s01g` | `GetTopologySnapshot` (contract and streaming endpoint for `Rva.Simulator`) with D15 detail levels, effective grid states, and locations | s01c, s01d | Not started | G-01..G-21 pass. Snapshots at a pinned `asOf` are byte-identical across calls. Docs: the "Domain (pending)" cells in `model-validation-scenarios.md` point at the tests in section 7. |

## 6. Test catalogue

Kinds: **S** success; **EF** expected failure (a `Result` error or 4xx, and nothing written); **UF** unexpected failure (asserts U1–U5 as they apply, with the named FI). Layers: **U** unit, **I** integration (handler or contract against real Postgres), **E** endpoint (host in memory against real Postgres), **A** architecture.

### p1-s01a — Foundation

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| A-01 | `Result`/`Result<T>` success and failure | S | U | `IsSuccess`, `Value`, `Error` behave as documented; `Value` on a failure throws `InvalidOperationException` |
| A-02 | Typed IDs and units: equality, conversions | S | U | Equal values are equal; `Kilowatts` keeps its sign (negative = charging) |
| A-03 | EF model matches the schema | S | I | Every mapped table and column exists in `rva_core` with a compatible type and nullability (from `information_schema`) |
| A-03b | History tables not mapped | S | I | No EF entity maps to a `*_history` table |
| A-04 | Lookups load | S | I | `LookupCatalog` resolves every seeded code in the seven lookup tables |
| A-05 | Change context recorded | S | I | A write through `UnitOfWork` produces a `_history` row with `changed_by` = the system actor (D17), `actor_type = system`, and the given `change_reason` |
| A-06 | Database-generated columns | S | I | `created_at`, `sys_period`, `grid_node_id` are filled by the database; the generated SQL never sets them |
| A-07 | Host wiring | S | E | The host starts with the module; `DbContext` and `IGridTopologyQueries` resolve; routes are mapped |
| A-08 | Readiness with a database | S | E | `/health` 200 and `/health/ready` 200 |
| A-10 | Unit of work without a change reason | EF | U | Validation error before any database call |
| A-11 | Two contexts update the same asset | EF | I | The second gets Conflict `GridTopology.ConcurrentChange` (xmin); one history version written |
| A-12 | Unique code violation (`23505` on `asset_code`) | EF | I | Classified as Conflict `GridTopology.DuplicateCode` |
| A-13 | Units given NaN or infinity | EF | U | `ArgumentOutOfRangeException` |
| A-20 | Database unreachable during a request | UF | E | FI-1: 503; U2, U3, U4 |
| A-21 | `SaveChanges` called outside `UnitOfWork` | UF | I | Throws `InvalidOperationException`; nothing written (U1) |
| A-22 | `SET LOCAL` fails (invalid actor value) | UF | I | Command fails; nothing committed; history unchanged (U1, U3) |
| A-23 | Database rejects a change the domain allowed | UF | E | FI-5 (kVA below power): 500; logged with the constraint name, the response without it (U1–U3) |
| A-24 | Unhandled exception in an endpoint | UF | E | Production: 500 problem+json with `traceId`, no stack trace (U2, U3) |
| A-25 | Cancellation during save | UF | I | FI-7: `OperationCanceledException`; nothing committed (U1); logged at Information |
| A-26 | Command timeout on a locked row | UF | I | FI-3: classified as transient; 503 at the endpoint; U1–U4 |
| A-27 | Missing lookup row | UF | E | FI-6: `/health/ready` 503 naming the lookup table and code; a command that needs it returns 500 (U2, U3); the host keeps running |
| A-28 | Host starts without a database | UF | E | FI-1: host starts; `/health` 200; `/health/ready` 503; the relay logs and backs off (U4) |
| A-29 | `GridTopology:UtilityTimeZone` missing or not a valid IANA id | UF | E | Host fails at startup, naming the key |

### p1-s01b — Catalog and seed

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| B-01 | Seed an empty database | S | I | Row counts match the seed files; the dev topology has at least 2 substations and 500 assets; every asset has at least one component |
| B-02 | Re-run the seed | S | I | No new rows; `_history` tables unchanged |
| B-03 | `ResolveTechnologyType(OpenADR_3.0, Storage, Battery)` | S | I | `bess` |
| B-04 | `GetProtocolMappings(IEEE_2030_5)` | S | I | Every row for that standard, with `ExtraAttributes` intact |
| B-10 | Unknown protocol mapping | EF | I | NotFound `GridTopology.ProtocolMappingNotFound`; never a default technology |
| B-11 | Blank or whitespace standard | EF | U | Validation error |
| B-12 | Seed value changed for an existing code | EF | I | Row unchanged (`DO NOTHING`); the seed's check query reports the drift |
| B-20 | Seed fails partway (invalid component row) | UF | I | One transaction: database left as before; `seed.sh` exits non-zero |
| B-21 | Seed against a database without the schema | UF | I | Fails at once with a clear message; non-zero exit |

### p1-s01c — Reads and constraint path

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| C-01 | `GetAsset` on fixture A | S | I+E | One asset, two components; rating 7.6 kVA, below the 20 kW component sum; makeup PV + storage; energy capacity 20 kWh; control manufacturer from the control profile |
| C-01b | Every `AssetSnapshot` field | S | I | For fixture A and a fixture D microgrid member, every field in section 3 has the expected value, including substation, site limits, and microgrid limits |
| C-01c | Customer segment independent of size | S | I | Fixture C (three Powerwalls) reads as `residential` |
| C-02 | `GetAsset` as of an earlier time | S | I | The old version; `ValidFrom` = `lower(sys_period)` of that version |
| C-03 | Constraint path, premise asset on fixture D's overloaded transformer | S | I | premise → transformer → feeder → substation in order, each with `GridNodeId`, level, code, rating; microgrid limits included (GT-05) |
| C-04 | Constraint path, primary-service premise | S | I | premise → feeder → substation; no transformer (GT-07) |
| C-05 | Constraint path, feeder-connected community battery | S | I | feeder → substation; no site limits |
| C-06 | Effective export limit | S | I | Asset limit above the premise cap → premise cap (GT-12); fixture B premise export 0 → 0 (GT-04); fixture C → 10 kW |
| C-06b | Effective import limit | S | I | Minimum of asset `\|p_min\|`, premise service capacity, and microgrid import limit; a microgrid import limit of 0 → 0 |
| C-06c | Null limit vs 0 (D20) | S | I | Null premise or microgrid limit → no limit at that level; 0 → zero |
| C-07 | Reparented premise | S | I | Paths as of before and after the move differ (GT-09) |
| C-08 | `GetAssetsBySubstation` | S | I | All assets under the substation at all four connection levels; none from other substations; as of a time when given |
| C-09 | `GetSubstationTree` on fixture D | S | E | Nested feeders → transformers → premises → assets; counts match the fixture |
| C-10 | Parity with `pg_temp.asset_paths` | S | I | For every asset in fixtures A–D, now and at an earlier time, the C# path equals the reference query |
| C-11 | Decommissioned asset | S | I | Still readable, current and as of, with state `decommissioned` |
| C-12 | Unrated feeder on the path (Q1) | S | I | `IsRated = false`; never an unbounded rating |
| C-13 | `ResolveNode` | S | I | Level and code resolve to the node's `GridNodeId`, now and as of a time |
| C-14 | Paging | S | I | `GetAssetsBySubstation` pages with a cursor; pages are stable and complete across the dev seed |
| C-15 | Dev topology paths | S | I | Every dev asset resolves a complete path to a rated substation; every node has a `grid_node_id` |
| C-16 | Locations | S | E | SRID 4326 coordinates round-trip as GeoJSON in the tree; a null location is allowed and returned as null |
| C-20 | Unknown asset | EF | I+E | NotFound `GridTopology.AssetNotFound` / 404 |
| C-21 | `asOf` before the asset existed | EF | I | NotFound |
| C-22 | `asOf` in the future | EF | E | 400 |
| C-23 | `asOf` malformed or without a UTC offset | EF | E | 400 |
| C-24 | Unknown substation code | EF | E | 404 |
| C-25 | `ResolveNode` with a code that exists only at another level | EF | I | NotFound |
| C-26 | Tree over the size limit; invalid page cursor | EF | E | 400 |
| C-30 | Incomplete path in the as-of view | UF | I | FI-6 (delete a `substations_history` row): `GridTopology.ConstraintPathIncomplete`, logged at Error; never a path missing a level (U5) |
| C-31 | Database down during a read | UF | E | FI-1: 503, no partial body (U2–U4) |
| C-32 | Read cancelled by the caller | UF | I | `OperationCanceledException` propagates; no Error log |
| C-33 | `GetConstraintPath` contract call with the database down | UF | I | FI-1: exception to the in-process caller, not a success result (U5) |
| C-34 | `GetConstraintPath` latency over the dev seed | S | I | Within the time budget per call (opt-in perf test; input for dispatch, R6) |

### p1-s01d — Grid state

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| D-01 | `EffectiveGridState` derivation, table-driven | S | U | No events → connected_energized; dead substation or feeder → de-energized below; islanded microgrid or `can_island` premise under a dead feeder → islanded; dead node below an island → de-energized; a later `connected_energized` below a dead node does not re-energize; asset `disconnected` overrides everything |
| D-02 | Parity with `pg_temp.asset_grid_states` and `dispatch_eligible` | S | I | Fixture D with a scripted event sequence, checked at several times: identical results (GT-18) |
| D-03 | Record an event | S | E | `occurred_at` stored as given; `recorded_at`, actor, and reason from the change context |
| D-04 | Late report | S | I | A read as of a time between occurred and recorded does not see it; a later read does |
| D-05 | Registry eligibility through `GetGridState` | S | I | Commissioned + active + energized (including islanded) → `IsRegistryEligible`; any other combination → not |
| D-10 | `occurred_at` in the future | EF | E | 400 |
| D-11 | Unknown target node or asset | EF | E | 422 |
| D-12 | Both targets or neither | EF | E | 400 |
| D-13 | `islanded` on a node that cannot island (transformer, feeder, premise without `can_island`) | EF | E | 422 (GT-17) |
| D-14 | Asset-targeted state outside D10 | EF | E | 422 |
| D-15 | Unknown grid state code | EF | E | 400 |
| D-16 | Missing reason (D11) | EF | E | 400 |
| D-20 | Update or delete an event | UF | I | FI-5: the database rejects it (append-only); classified unexpected and logged; the module exposes no such path |
| D-21 | App clock ahead of the database clock | UF→EF | E | FI-8: the database's not-future check fires; returned as the same 400 as D-10, not 500 (Q2) |
| D-22 | `GetGridState` with the database down | UF | I | FI-1: exception, never "energized" (U5) |

### p1-s01e — Asset writes

Every EF row also asserts that nothing was written: no asset, component, history, or outbox row.

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| E-01 | Register a fixture-A-shaped asset (BTM, two components) | S | E | 201 with `Location`; planned, not_enrolled; history with actor and reason; one `AssetRegistered.v1` outbox row in the same transaction, carrying the D19 fields |
| E-02 | Register a feeder-connected FTM community battery | S | E | 201 |
| E-03 | Register a member of a feeder microgrid | S | E | 201 |
| E-03b | Register a member of a substation microgrid | S | E | 201 |
| E-04 | Sign conventions: PV-only (`p_min = 0`), EVSE (`p_max = 0`), V2G, battery | S | E | All accepted (GT-11) |
| E-05 | Lifecycle install → commission → decommission | S | E | Each step versioned; `AssetUpdated` then `AssetDecommissioned`; the row is kept and its code stays reserved |
| E-06 | Participation through every D8 transition | S | E | Each versioned with its reason; one `AssetUpdated` each |
| E-07 | Repeat a command already in effect (D9) | S | E | 200; no history row; no event |
| E-08 | Identical re-registration (D9) | S | E | 200; no new row or event |
| E-09 | Dev topology meets the domain invariants | S | I | Every dev seed asset passes the domain's FTM, microgrid membership, and component rules |
| E-10 | Duplicate code with a different body, including a decommissioned asset's code | EF | E | 409 (GT-10) |
| E-11 | Zero or two connection points | EF | E | 400 |
| E-12 | Unknown connection node | EF | E | 422 |
| E-13 | BTM or ATM asset at a transformer, feeder, or substation, including as a microgrid member | EF | E | 422 (GT-06) |
| E-14 | Member outside its feeder microgrid's feeder | EF | E | 422 (GT-07) |
| E-14b | Member under a different substation than its substation microgrid | EF | E | 422 |
| E-15 | No components, or more than the maximum (100) | EF | E | 400 |
| E-16 | Component technology differs from its profile; control-only profile used as a component | EF | E | 422 |
| E-16b | Unknown control profile | EF | E | 422 |
| E-16c | No control profile (D21) | S | E | 201; `ControlManufacturer = null` |
| E-17 | `p_min > p_max`; power above kVA; kVA ≤ 0; negative export limit; `unit_count` ≤ 0 | EF | E | 400 naming each field |
| E-18 | Malformed JSON, wrong types, missing fields, NaN/Infinity, code over 50 characters | EF | E | 400 |
| E-19 | Commission without an in-service date, or with a future one in the utility time zone | EF | E | 400; today's local date is accepted just after local midnight even while UTC is a day behind or ahead (D18) |
| E-20 | Decommission an active or suspended asset | EF | E | 409 (GT-16) |
| E-21 | Any change to a decommissioned asset | EF | E | 409 |
| E-22 | Active or suspended participation on a non-commissioned asset | EF | E | 409 (GT-16) |
| E-23 | Suspend without a reason | EF | E | 400 |
| E-24 | Decommissioned date before the in-service date | EF | E | 400 |
| E-25 | Backward lifecycle change (commissioned → installed) | EF | E | 409 |
| E-26 | Unknown asset in the route | EF | E | 404 |
| E-30 | Two concurrent registrations with the same code and different bodies | UF→EF | E | One 201, one 409; exactly one asset and one outbox row |
| E-31 | Two concurrent lifecycle changes | UF→EF | E | One success, one 409 `ConcurrentChange`; one new history version |
| E-32 | Outbox write fails | UF | E | FI-4: 500; no asset, history, or outbox row (U1–U4) |
| E-33 | Connection killed after the asset insert, before commit | UF | E | FI-2: 503; nothing committed (U1–U4) |
| E-34 | Request cancelled during commit | UF | E | FI-7: either fully committed with its outbox row, or nothing; never an asset without its event |
| E-35 | Database rejects a lifecycle change the domain allowed | UF | E | FI-5 on the lifecycle trigger: 500; logged as a validation gap (U1–U3) |
| E-36 | Invariant sweep after the E-3x tests | UF | I | Every asset version in `assets_history` and `assets` has a matching outbox event |

### p1-s01f — Outbox relay

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| F-01 | Relay publishes pending rows | S | I | In `occurred_at` order; each handed to `IIntegrationEventPublisher`; `published_at` set |
| F-02 | End to end | S | E | `RegisterAsset` → a handler registered by a test module receives `AssetRegistered` with matching fields within the timeout |
| F-03 | Serialization round trip for each event type and version | S | U | Fields and `event_id` preserved |
| F-04 | Relay restart | S | I | Rows left pending before the restart are delivered after it |
| F-05 | Per-asset order | S | E | Register → commission → decommission delivered in that order |
| F-06 | Retention | S | I | Published rows older than 7 days are deleted; unpublished and parked rows are kept |
| F-10 | Unknown `event_type` (a row from a newer version) | EF | I | `attempt_count` incremented, logged at Warning; later rows still delivered; parked after 10 attempts, logged at Error once (D13) |
| F-11 | Payload that does not deserialize | EF | I | Same as F-10 |
| F-20 | Publisher throws | UF | I | FI-4: `published_at` stays null, `attempt_count` incremented, retried with backoff, then published once it succeeds |
| F-21 | Database unavailable while polling | UF | I | FI-1: logs, backs off, keeps running; resumes when the database returns (U4) |
| F-22 | Crash after publish, before marking published | UF | I | FI-2: redelivered with the same `event_id` (at-least-once; consumers can deduplicate) |
| F-23 | Two relays at once | UF | I | `SKIP LOCKED`: every row published exactly once |
| F-24 | Host shutdown during a batch | UF | E | Stops within the shutdown timeout; no row marked published that was not handed over |
| F-25 | Bug-type exception inside the loop | UF | E | FI-4 throwing `NullReferenceException`: logged at Error, the loop continues, the host keeps running (U4) |

### p1-s01g — Topology snapshot

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| G-01 | Substation scope, `assets` detail | S | E | Every node and commissioned asset in the subtree, with `GridNodeId`s and ratings; matches `pg_temp.scope_assets` |
| G-02 | Feeder, transformer, premise, and microgrid scopes | S | I | Each matches `pg_temp.scope_assets` for fixture D |
| G-03 | `nodes` and `components` detail | S | I | `nodes` has no assets; `components` adds components |
| G-04 | Pinned `asOf` across an edit | S | I | Byte-identical output before and after the edit (SIM-11) |
| G-05 | 50,000-node generated fleet | S | I | Within the time budget (opt-in perf test, not run by default) |
| G-06 | Effective grid states in the snapshot | S | I | Each asset's state matches `pg_temp.asset_grid_states` at `asOf` |
| G-07 | Locations in the snapshot | S | E | GeoJSON points and feeder lines in SRID 4326 |
| G-10 | Unknown scope root, or a code that exists only at another level | EF | E | 404 |
| G-11 | Invalid level or detail | EF | E | 400 |
| G-12 | `asOf` in the future / before the root existed | EF | E | 400 / 404 |
| G-20 | Client disconnects mid-stream | UF | E | Query stopped, connection released, no Error log |
| G-21 | Database fails mid-stream | UF | E | FI-2: the response ends without its closing completeness marker, so the client can detect truncation; logged at Error |

### Cross-cutting

| ID | Case | Kind | Layer | Expected |
|---|---|---|---|---|
| X-01 | Architecture suite with the new code | S | A | Passes: `DbContext` internal, only `GridTopologyModule` public, no ORM attributes, no forbidden folders |
| X-02 | Test packages | S | A | Only approved packages (after D1) |
| X-03 | Every public error code is tested | S | U | Each constant in `GridTopologyErrors` appears in at least one assertion |
| X-04 | Problem+json everywhere | S | E | Every 4xx and 5xx from every endpoint is `application/problem+json` with `code` and `traceId` |

## 7. Traceability to the validation scenarios

Every "Domain (pending)" item in `docs/architecture/model-validation-scenarios.md` that GridTopology owns:

| Scenario | Pending item | Tests |
|---|---|---|
| GT-04 | effective ceiling computation | C-06, C-06c |
| GT-05 | `GetConstraintPath` | C-03, C-10, C-30 |
| GT-06 | non-FTM above the meter | E-13 |
| GT-07 | cross-feeder membership | E-14, E-14b (C-04 for service paths) |
| GT-12 | effective limit computation | C-06, C-06b |
| GT-16 | multi-program enrollment | out of scope (later decision); the participation rules are E-06, E-20, E-22 |
| GT-17 | islanding only where the node can island | D-13 |
| GT-18 | derivation in GridTopology | D-01, D-02, D-05 |
| GT-18 | real-time availability | out of scope (telemetry, Step 3) |

Simulation owns these. The GridTopology contract tests support them, but they stay open until Step 2: SIM-01 (root code at the right level: C-13, C-25), SIM-03 (asset in the run's topology: C-21, G-01), SIM-07 (validate as of `topology_as_of`: C-02, C-21).

## 8. Risks

| Risk | Mitigation in this step | Still needed later |
|---|---|---|
| **R1** No authentication or RBAC. Both writes and reads are open, and the tree and snapshot expose the full topology, which NERC CIP-011 likely treats as BES Cyber System Information. | Development binds to localhost only. No shared deployment before auth. | Phase 2 service JWT must cover the simulator's snapshot calls. Phase 3 must pass the authenticated user or service into `SET LOCAL rva.actor` in place of the system actor (D17). Neither is in `MASTER_PLAN.md` yet. |
| **R2** Delivery is at-least-once only up to the in-process dispatcher (D13). A failed handler loses its event, and Telemetry's `known_assets` drifts without anyone noticing. | `event_id` lets consumers deduplicate; `GetAssetsBySubstation` can rebuild a read model. | Step 3 should require that Telemetry can rebuild `known_assets` from `GetAssetsBySubstation` and check for drift. A dispatcher retry later. No phase covers this yet. |
| **R3** Each module needs its own outbox relay, since the shared kernel cannot hold EF code. | — | Extract shared code when Simulation needs a relay (Step 2). |
| **R4** The .NET integration tests depend on the pgTAP test image and psql to load fixtures. | Intended: one source of fixtures for both suites. | — |
| **R5** No migration tool. EF maps onto an init script that cannot upgrade a live database (D6), and `PHASE_1_PLAN.md` requires a tool before the first change to a deployed schema. | — | No phase schedules it. It is needed before the Phase 4 cloud and IaC work at the latest. |
| **R6** Constraint path latency. Dispatch must read limits fresh through the contract, one asset at a time. | C-34 measures it. | A batch `GetConstraintPaths(assetIds, asOf)` before dispatch is built. |
| **R7** Audit scope. Lifecycle and participation changes decide what dispatch may command, and the history tables are their only record. | History tables with actor and reason. | Phase 4 must decide whether topology changes also go through `IAuditProvider`. |

## 9. Verification

- Per part: `dotnet build VppAggregator.slnx`, `dotnet test --solution VppAggregator.slnx` (the integration project needs Docker), `infrastructure/postgres/tests/run_tests.sh`, and the `ci` workflow on the PR.
- The test catalogue is the checklist: the PR description lists the part's test IDs and their results.
- After `s01e`: `docker compose up`, `seed.sh`, `dotnet run --project src/Rva.Backend/Rva.Host`; register, commission, and read an asset with curl; check its history and outbox rows in `rva_core`.
- Mutation check (as in `p1-s00f`): remove one domain rule (for example the FTM rule) and confirm its EF test fails and the database backstop test reports it as unexpected.
- Agents working in a Claude Code sandbox cannot run the tests locally unless `/sandbox` allows local binding (the test runner's pipe) and the Docker socket. Their PRs say so, and the results come from CI or a local run by a human.
