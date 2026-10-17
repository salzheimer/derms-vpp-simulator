# GridTopology and Simulator Model Validation Scenarios

## Purpose

Use these scenarios to check that the GridTopology and Simulation data models can represent the Phase 1 cases RVA needs before implementing the backend modules or Chaos Engine. Each case states the fixture, the model behavior to inspect, and the expected result. The database-owned parts are executable pgTAP tests (see [Executable tests](#executable-tests)); the parts owned by a backend module are tested in that module once it exists.

The relational schema is defined in `infrastructure/postgres/core/rva_core_init.sql`; the domain decisions and reference topologies are in `.ai/plans/PHASE_1_PLAN.md`. The database schema is authoritative where this document and the schema differ.

## Validation approach

For each scenario, check that:

1. The topology can be represented without inventing fields or overloading unrelated fields.
2. Invalid configurations are rejected at the layer that owns the invariant (database constraint or GridTopology domain rule).
3. A run can identify its scope, topology version, initial state source, and per-asset overrides sufficiently to reproduce its inputs.
4. Results remain interpretable at the premise, transformer, feeder, and substation levels. Fleet totals alone are insufficient when a local node is constrained.

Use synthetic values for model validation. Utility feeder maps and operational ratings are not assumed to be public or suitable as authoritative test fixtures. Public utility filings or GIS layers may inform later realism checks, but they will need normalization and utility validation before serving as ground truth.

## Executable tests

Each scenario has a pgTAP file in `infrastructure/postgres/tests/pgtap/` named after its ID, and every assertion is prefixed with that ID. Fixtures A–D are in `infrastructure/postgres/tests/fixtures/`. Run everything with `infrastructure/postgres/tests/run_tests.sh` (`.ai/scripts/verify.md`); the conventions are in `.ai/rules/sql-ddl.md` section 7.

Each expectation is tested where its owner enforces it:

* **Database:** a normal pgTAP assertion, which must pass.
* **TODO (gap):** the scenario expects it but the DDL does not enforce it yet. It is written as a pgTAP TODO test, which reports `not ok # TODO` without failing the run. Each one needs a decision: enforce it in the DDL, or move the rule to the owning module and update this document. None are open today; resolved ones are listed under [Coverage boundary and open decisions](#coverage-boundary-and-open-decisions).
* **Domain (pending):** owned by GridTopology or Simulation, usually because it crosses a module boundary that has no foreign key on purpose. A `boundary:` test pins that the database accepts the input. The rule itself is tested in the module (xUnit with a fake `IGridTopologyQueries`) once the module exists.

| Scenario | Test file | Database | TODO (gap) | Domain (pending) |
|---|---|---|---|---|
| GT-01 | `gt01_hierarchy_identity.sql` | node identity, parent links, connection cardinality | | |
| GT-02 | `gt02_shared_inverter.sql` | asset vs component, technology/profile match | | |
| GT-03 | `gt03_separate_control.sql` | two assets per premise, shared site limit | | |
| GT-04 | `gt04_site_cap.sql` | site cap stored and on every asset's path; 0 ≠ NULL | | effective ceiling computation |
| GT-05 | `gt05_local_overload.sql` | constraint path and scope aggregates | | `GetConstraintPath` |
| GT-06 | `gt06_connection_interconnection.sql` | connection cardinality | | non-FTM above the meter |
| GT-07 | `gt07_service_path_microgrid.sql` | service paths, PCC cardinality | | cross-feeder membership |
| GT-08 | `gt08_history_snapshot.sql` | versioning, actor/reason, as-of snapshots | | |
| GT-09 | `gt09_reparenting.sql` | reparenting history and as-of paths | | |
| GT-10 | `gt10_code_reuse.sql` | decommissioned codes stay reserved; corrected codes can be reused | | |
| GT-11 | `gt11_sign_conventions.sql` | bounds and kVA checks per technology | | |
| GT-12 | `gt12_nested_limits.sql` | every limit reachable on the path | | effective limit computation |
| GT-13 | `gt13_microgrid_pcc.sql` | PCC flags and limits | | |
| GT-14 | `perf/gt14_fleet_snapshot.sql` (`--perf`) | 600-asset as-of snapshot under 2 s (rough guard) | | |
| GT-15 | `gt15_asset_lifecycle.sql` | lifecycle states, field dates, terminal decommission, delete only as correction | | |
| GT-16 | `gt16_participation.sql` | participation separate from lifecycle, versioned | | multi-program enrollment |
| GT-17 | `gt17_grid_state_events.sql` | grid state events: targets, append-only, field vs recorded time, latest state as of | | islanding only where the node can island |
| GT-18 | `gt18_effective_grid_state.sql` | effective asset state and dispatch eligibility (reference implementation) | | derivation in GridTopology; real-time availability |
| SIM-01 | `sim01_scope_detail.sql` | scope/detail lookups, subtree resolution | | root code exists at the right level |
| SIM-02 | `sim02_initial_state.sql` | synthetic vs measured start, timestamp checks | | telemetry available at the timestamp |
| SIM-03 | `sim03_overrides.sql` | override bounds, uniqueness, Pending-only | | asset in the run's topology |
| SIM-04 | `sim04_lifecycle.sql` | events, append-only, fixed inputs (scope at creation, window and configuration after Pending), transitions, status/timestamp consistency | | transitions also in the `SimulationRun` entity |
| SIM-05 | `sim05_window_cutoff.sql` | topology cutoff, window independent of execution | | |
| SIM-06 | `sim06_fault_targets.sql` | target identity and descendant sets | | `configuration_json` contract |
| SIM-07 | `sim07_asof_override_validity.sql` | decommissioned asset still in the as-of scope | | validate against `topology_as_of` |
| SIM-08 | `sim08_microgrid_scope.sql` | microgrid members resolvable; microgrid scope level | | |
| SIM-09 | `sim09_window_edges.sql` | DST length, half-open bounds, inverted, empty, and start-less windows | | |
| SIM-10 | `sim10_failure_reason.sql` | actor captured; Failed requires a reason | | |
| SIM-11 | `sim11_reproducibility.sql` | identical snapshots for the same `topology_as_of` | | |

## Reference topology fixtures

Create these as isolated test fixtures, not as part of the development seed. They are the four topologies described in the Phase 1 plan. Every fixture asset is registered commissioned (in service since 2024-06-01) and active, so it is part of the operating grid; tests that need other states change them.

| Fixture | Topology shape | What it probes |
|---|---|---|
| A — Hybrid home | One residential premise; one asset controlled through a shared inverter; battery and PV components; asset kW/kVA rating is lower than the component sum | Asset versus component distinction; shared inverter cap; BTM connection; component technology/profile consistency |
| B — Non-export warehouse | One C&I premise with zero site export limit; separate PV and battery assets; premise can island | Site limit versus per-asset limits; customer segment independent of equipment; premise-level island capability |
| C — Large residential system | One residential premise with three battery units and a 10 kW site export cap | Equipment size does not determine customer segment; site export cap can bind below asset capability |
| D — Community microgrid | Feeder microgrid with 20 homes, a school, mixed vendor assets, a feeder-connected community battery, and one overloaded neighborhood transformer | Scope aggregation; feeder versus premise assets; microgrid ownership and membership rules; local bottleneck hidden by feeder-wide capacity |

## Scenario catalog

### GT-01 — Normal hierarchy and stable node identity

**Setup:** Build the standard hierarchy: substation → feeder → neighborhood transformer → premise. Add a feeder-primary premise and a feeder- or substation-connected asset where appropriate.

**Validate:** Every physical node belongs to the correct node type; parent links resolve; each node receives a unique `grid_node_id`; each connection has exactly one allowed connection point.

**Expected:** Valid chains are represented and queryable. A node's `grid_node_id` is stable and remains registered after the node row is removed. Mismatched node type, missing parent, and multiple or absent connection points are rejected.

### GT-02 — One control interface, multiple technologies, shared rating

**Setup:** Use Fixture A: one control interface and asset with battery and PV components. Give the components a combined nameplate capacity greater than the shared inverter / asset rating.

**Validate:** Components carry technology, profile, unit count, installed power and energy; the asset carries connection point, control profile, dispatch bounds, and its own kVA rating.

**Expected:** The model preserves both component capacities for simulation physics while the asset rating remains the controlling grid-facing limit. The asset is not duplicated just because it has multiple component technologies. A component whose technology does not match its hardware profile is rejected.

### GT-03 — Separate control interfaces at one premise

**Setup:** Use Fixture B: PV and battery with separate control interfaces at the same premise.

**Validate:** Register two assets at one premise, each with its own control hardware profile, limits, and components.

**Expected:** The model does not merge independently controlled systems into one asset. Both assets inherit the same premise-level service and export constraints for constraint evaluation.

### GT-04 — Site cap binds while system and node headroom remain

**Setup:** Use Fixture C. Set equipment and asset capability above the premise's export cap; choose substation and transformer ratings high enough not to bind.

**Validate:** Topology snapshots and constraint paths expose premise and asset limits separately.

**Expected:** The applicable export ceiling is the premise cap (10 kW), even though the asset fleet could export more. Residential classification remains unchanged by the installed capacity. A zero premise export limit in Fixture B means no export, not an unknown/unset limit.

### GT-05 — Local transformer overload hidden by aggregate headroom

**Setup:** Use Fixture D. Give one neighborhood transformer connected inverter capacity above its rating while keeping the whole feeder's aggregate connected capacity below feeder and substation ratings.

**Validate:** Ask GridTopology for constraint paths for assets below the overloaded transformer and for assets elsewhere on the feeder. Aggregate assets at transformer, feeder, and substation scopes.

**Expected:** The model retains the transformer limit and identifies it as a constraint for its descendants. A fleet- or feeder-only total must not imply that transformer-connected assets are unconstrained. This case validates topology and limit inputs; power-flow behavior is out of Phase 1 scope.

### GT-06 — Connection-point and interconnection consistency

**Setup:** Create assets at premise, transformer, feeder, and substation connection points, including both valid and deliberately invalid interconnection types.

**Validate:** Exactly one connection point is set. Premise assets may use BTM, FTM, or ATM as permitted; transformer-, feeder-, and substation-connected assets are FTM.

**Expected:** Invalid connection cardinality is rejected by the database. Non-FTM assets above the premise meter are rejected by the GridTopology domain validation. A premise-connected asset may be BTM; the model does not infer interconnection type from voltage or island capability.

### GT-07 — Premise service path and microgrid membership

**Setup:** Model a normal secondary-service premise connected to a neighborhood transformer, a primary-service premise connected directly to a feeder, a premise-level island-capable site, and Fixture D's utility-side microgrid.

**Validate:** Premises connect to exactly one of transformer or feeder. Utility microgrids connect to exactly one of feeder or substation. Their member assets lie under the microgrid's own feeder or substation.

**Expected:** Both premise service configurations are representable. A single-site BTM microgrid is represented by the premise's `can_island`, not a utility microgrid row. Invalid cross-feeder membership is rejected by domain validation.

### GT-08 — Historical topology and reproducible snapshot

**Setup:** Register an asset and topology; change its export cap, component rating, or parent assignment; request snapshots at instants before and after the change.

**Validate:** Versioned rows archive prior values with validity periods; snapshots accept a scope root, detail level, and `asOf` time; each asset includes its validity start.

**Expected:** The earlier snapshot reflects the earlier topology and the later snapshot reflects the updated topology. Repeating the same query for the same `asOf` yields the same topology inputs. `grid_nodes` identity history is not treated as mutable topology history.

### GT-09 — Load transfer / reparenting

**Setup:** Use Fixture D. Move `FXD-HOME-05` from the overloaded `FXD-XFMR-1` to `FXD-XFMR-2` with a change reason.

**Validate:** The premise's history row keeps the old parent and the reason; snapshots before and after resolve different constraint paths; the moved premise's asset is not edited.

**Expected:** Before the transfer, `FXD-XFMR-1` serves five homes; after, four. The asset's constraint path follows its premise. This is the most common topology edit in distribution operations, so callers must never cache a path across topology versions.

### GT-10 — Decommission versus correction, and code reuse

**Setup:** Decommission one asset (GT-15) and delete another as a correction of a duplicate registration; then register new assets with both codes.

**Validate:** Whether each code can be reused, and how history identifies the assets.

**Expected:** A decommissioned asset keeps its row, so its code stays reserved and reuse is rejected. A record deleted as a correction never described a real asset, so its code can be reused by a new asset with a new `asset_id`. Across history a corrected code can then name two ids, so history and telemetry key on `asset_id`.

### GT-11 — Sign conventions by technology

**Setup:** Register a PV-only asset (`p_min = 0`), a managed EV charger (`p_max = 0`), a bidirectional V2G charger with asymmetric bounds, and invalid assets with flipped bounds or a charging magnitude above `rated_kva`.

**Validate:** Positive is export/discharge and negative is import/charge; bounds and kVA checks hold for every technology.

**Expected:** The valid cases are accepted, and a flexible load keeps a non-zero range distinct from "no capability". Flipped bounds and over-kVA charging are rejected by the database.

### GT-12 — Nested limits

**Setup:** In Fixture C, give one asset a per-system export limit above the premise cap. In Fixture D, set the microgrid PCC export limit to 0.

**Validate:** Every limit on an asset's path (asset, premise, microgrid PCC) is reachable from one path query.

**Expected:** The database accepts each limit as recorded. The effective limit is the minimum along the path: 10 kW for the Fixture C asset, and 0 export for the 500 kW community battery behind a non-export PCC. GridTopology computes it; the database must not hide any input.

### GT-13 — Microgrid PCC flags and limits

**Setup:** Register a grid-tied (non-islanding) microgrid with zero import and export limits; attempt negative limits.

**Validate:** `can_island = false` is representable; 0 is stored as 0, not NULL.

**Expected:** Zero means zero (non-import or non-export), and NULL means no limit recorded. Negative limits are rejected by the database.

### GT-14 — Fleet-scale snapshot

**Setup:** Generate one substation with 4 feeders, 100 transformers, and 600 premises with one asset each, then update every asset so the `_as_of` functions read both current and history rows.

**Validate:** A substation-scope snapshot through the `_as_of` functions returns every asset with its full path within a time budget.

**Expected:** 600 assets in under 2 s. This is a rough regression guard for the 500+ DER target: at this size it catches gross slowdowns in the `_as_of` path (for example a pathological join), not subtle ones such as a lost period index. It runs with `--perf` and is not a gate, because wall-clock budgets vary by machine. Scale the fixture up when the target grows.

### GT-15 — Asset lifecycle and decommissioning

**Setup:** Register an asset without status, move it planned → installed → commissioned, then decommission a Fixture C battery. Attempt invalid dates, reviving a decommissioned asset, deleting it, deleting without a reason, and correcting a mistaken registration.

**Validate:** Lifecycle state and field dates (`in_service_date`, `decommissioned_date`) on the asset, enforced by `topology.check_asset_lifecycle`; versioned history; run scope.

**Expected:** A new asset defaults to planned and not enrolled, so it cannot be dispatched by accident. Commissioning needs an in-service date; planned and installed assets have none; `decommissioned_date` is set exactly when decommissioned, and not before entering service. Decommissioning is a state change: the asset stays in the registry, history records an `UPDATE` with its reason, earlier snapshots still show it commissioned, and it leaves run scopes. Decommissioned is terminal and cannot be deleted. `DELETE` is only for records entered in error, needs a change reason, and is archived as a `DELETE`, so the two are always distinguishable. The field dates record when things happened in the field, not when they were entered in RVA, so a replay of a past period can tell which assets existed then.

### GT-16 — Program participation separate from lifecycle

**Setup:** In Fixture C, un-enroll a commissioned battery, suspend another with a reason, and try to activate an installed asset or decommission an active one.

**Validate:** Participation (`not_enrolled`, `active`, `suspended`, `opted_out`) is separate from lifecycle and versioned.

**Expected:** Commissioned but not enrolled is representable: physically in service, not usable by the VPP. Only a commissioned asset can be active or suspended, and an active asset must leave its program before it is decommissioned. Suspension is versioned with its reason. If one asset can be in several programs, participation moves to an enrollment table (domain decision).

### GT-17 — Grid connectivity and energization state events

**Setup:** In Fixture D, record a feeder trip and restoration, an asset disconnect reported late, and invalid events (two targets, no target, a future time); try to edit or delete events.

**Validate:** `grid_state_events` targets exactly one grid node or asset; `grid_states` has one row per valid combination of connectivity and energization; `topology.latest_grid_states(t)` returns each target's latest state.

**Expected:** The four states are connected_energized (normal), connected_deenergized (upstream outage), islanded (energized from local sources), and disconnected. Events are append-only, with actor and reason from `SET LOCAL rva.*`. Each event keeps two times: `occurred_at` (in the field) and `recorded_at` (set by trigger). A read as of t sees only events recorded by t, so it is reproducible, and a late report keeps its field time. A target with no events is in its normal state. Whether a node may island at all (a microgrid, or a premise with `can_island`) is a domain rule.

### GT-18 — Effective grid state and dispatch eligibility

**Setup:** In Fixture D, plus a non-member premise on `FXD-FDR`: trip the feeder, island the school premise, island the microgrid, fault a transformer inside it, disconnect one asset, and suspend another.

**Validate:** Each asset's effective state, derived from the events along its path, and dispatch eligibility (commissioned, active, and energized).

**Expected:** With no events every asset is connected and energized. A feeder outage de-energizes everything below it and nothing on a sibling feeder. An islanded premise or microgrid re-energizes its own assets from local sources, while non-members stay de-energized. A transformer fault inside an island de-energizes its homes. An asset's own disconnect overrides its path. Disconnected, suspended, and de-energized assets are not eligible; islanded and grid-energized active assets are. Derivation belongs to GridTopology (`GetConstraintPath` and a future energized-path model). `pg_temp.asset_grid_states` and `pg_temp.dispatch_eligible` in the test helpers are its executable spec. Real-time availability (online, maintenance) comes from telemetry and is checked separately.

### SIM-01 — Run scope and detail level combinations

**Setup:** Create runs scoped at substation, feeder, transformer, and premise levels. Request `hierarchy`, `assets`, and `assets_with_components` details where meaningful.

**Validate:** A scope root code resolves through GridTopology at the run's `topology_as_of`; snapshots contain precisely the requested subtree and detail.

**Expected:** Unknown root codes, wrong root levels, and unsupported detail values fail run creation. Hierarchy-only runs omit assets; asset runs include assets; component-detail runs include component data. Scope does not silently expand to a different parent.

### SIM-02 — Synthetic start versus measured initial state

**Setup:** Create one run with no `initial_state_as_of` and one with a timestamp for a known telemetry snapshot. Give one battery an explicit starting state-of-charge override.

**Validate:** Distinguish synthetic initialization from measured initialization and preserve the run's fixed inputs.

**Expected:** Null means initialize from simulator defaults plus overrides. A measured-state run records the requested timestamp and uses data available as of that instant. The explicit override is applied to the specified asset. A timestamp later than run creation is rejected by the schema; availability of telemetry at that timestamp is validated by the application/integration path.

### SIM-03 — Per-asset override bounds and identity

**Setup:** Add overrides for assets in the scoped topology, including available/unavailable, narrowed power bounds, and state of charge at 0%, an interior value, and 100%. Attempt duplicate asset state, inverted bounds, out-of-range state of charge, and an asset not present in topology.

**Validate:** One state row per `(simulation_run_id, asset_id)`; bounds are ordered; state of charge is in [0, 100]; the asset belongs to the run's resolved topology.

**Expected:** Duplicate, inverted, and out-of-range values are rejected by database constraints. Out-of-scope or unknown asset references are rejected through GridTopology contract validation because the asset ID intentionally has no cross-schema foreign key. Overrides can only change while the run is Pending.

### SIM-04 — Lifecycle event history and immutable inputs

**Setup:** Create a run, transition Pending → Running → Completed, then try to revise fixed inputs or edit/delete its lifecycle events. Also cover Pending → Failed for a pre-start failure.

**Validate:** Status transitions, `started_at` / `ended_at`, trigger-written append-only events, actor/reason capture, and run-input immutability after leaving Pending.

**Expected:** Each status change produces exactly one event; events cannot be updated or deleted; invalid lifecycle transitions are rejected; a failed-before-start run has no `started_at` and records failure time/reason. Scope, run kind, topology version, and initial-state timestamp cannot drift after creation.

### SIM-05 — Simulation time window and topology cutoff

**Setup:** Create a bounded simulated window and select a topology version before an asset update. Repeat with an unbounded window if supported by the run contract.

**Validate:** Simulation time is distinct from execution wall-clock timestamps. The selected topology version is no later than run creation and is used to resolve scope/assets.

**Expected:** A run reads topology at its fixed `topology_as_of`, independent of later edits. The simulated window represents the time being modeled, not the time the worker executes. If the API requires a non-empty or half-open range, that rule must be made explicit in its validation contract; the current column is nullable `TSTZRANGE`.

### SIM-06 — Faults at several grid levels

**Setup:** Configure synthetic chaos inputs in Fixture D for: one asset communication loss, multiple assets on one transformer, a feeder-wide voltage or thermal condition, and a substation-wide communication interruption. Include recovery after a finite duration.

**Validate:** Each fault identifies the target by stable asset/node code or ID, fault type, start/duration, and affected scope; generated telemetry can be associated with the run and asset.

**Expected:** Fault targeting is unambiguous at both asset and node levels; localized faults do not unintentionally affect sibling branches; a parent-level outage affects descendants by defined propagation rules. The current `configuration_json` can carry scenario rules, but Phase 1 must define and validate its JSON contract before execution. Fault outcomes belong to the Simulator process; Simulation owns the run ledger and overrides.

### SIM-07 — As-of override validity

**Setup:** Pin a run's `topology_as_of` before an asset is decommissioned; add an override for that asset.

**Validate:** The asset is out of the current scope but in the run's as-of scope.

**Expected:** Override validation resolves assets against the run's `topology_as_of`, not current rows, so a run can be reproduced after the asset is decommissioned. The database supplies the as-of snapshot; GridTopology performs the check.

### SIM-08 — Microgrid-scoped run

**Setup:** Create an islanding scenario for Fixture D scoped to `FXD-MG`.

**Validate:** The microgrid's members resolve as a scope, and a run can be created at that scope.

**Expected:** A feeder scope would include non-members, so islanding scenarios need a microgrid scope; `simulation.scope_levels` has a `microgrid` row, and `scope_root_code` is then a `microgrid_code`.

### SIM-09 — Simulated window edges

**Setup:** Create runs whose windows span the November 2025 fall-back DST change (America/Los_Angeles), are half-open, end before they start, are empty, or have no start.

**Validate:** What `simulated_window` accepts and how its length is measured.

**Expected:** The DST day keeps its true 25-hour length, half-open bounds are preserved so consecutive windows do not overlap, and inverted, empty, and start-less windows are rejected. An open end is allowed.

### SIM-10 — Failure reason and actor

**Setup:** Move a run to Running with a reason, then to Failed without setting `rva.change_reason`.

**Validate:** Lifecycle events capture the actor and reason.

**Expected:** The actor is recorded from `SET LOCAL rva.actor`, and a run cannot enter Failed without a reason, because a failure audit trail without causes is weak evidence (`.ai/rules/security.md`).

### SIM-11 — Reproducibility pair

**Setup:** Create two runs with the same scope and the same pinned `topology_as_of`, one before and one after a transformer re-rating and a premise transfer.

**Validate:** Both runs resolve their scoped topology through the `_as_of` functions.

**Expected:** Both snapshots are identical, and the later run sees the pre-edit rating and parent. Creation time does not affect a run's inputs once `topology_as_of` is fixed.

## Coverage boundary and open decisions

- The topology stores the normal hierarchy plus grid state events (GT-17), not switches or an electrical network. Effective energization is derived along the normal hierarchy (GT-18); a future network model can derive it from the switched path behind the same contract. GT-05 validates static ratings and constraint-path selection only; it cannot prove voltage, thermal flow, or contingency results.
- The schema has hierarchy and node capacity fields but no electrical line, switch, phase, impedance, or power-flow model. Validate these in a future digital-twin phase.
- `configuration_json` has no schema in the database. Define versioning, required fields, fault targeting, random seed, and reproducibility guarantees before treating chaos scenarios as executable contracts.
- Public grid data can be used as a realism reference, but not presumed as complete or authoritative for private distribution assets. Keep fixtures synthetic, documented, and stable for model tests.
- These scenarios validate model coverage and constraints. They do not define operational dispatch policy, protection settings, or utility-approved limits.
- Node codes are unique within a level, not across levels (SIM-06): a transformer may share a feeder's code. Fault targets and scope roots must carry the level or a `grid_node_id`.

**Decisions from the first test run (all resolved and enforced).** Each started as a TODO test and was fixed test-first:

| Gap found | Scenario | Resolution |
|---|---|---|
| Scope, `simulated_window`, and `configuration_json` could change after creation | SIM-04 | DDL: scope fixed at creation; window and configuration fixed once the run leaves Pending (`simulation_runs_bookkeeping`) |
| Invalid transitions (Completed → Running, Failed → Pending) were accepted | SIM-04 | DDL backstop (`simulation.check_run_lifecycle`) behind the `SimulationRun` entity: created Pending; Pending → Running → Completed, or → Failed; Completed and Failed terminal |
| Status and timestamps could disagree | SIM-04 | DDL: `started_at` set exactly once started, `ended_at` exactly once ended |
| A run could not be scoped to a microgrid | SIM-08 | DDL: `microgrid` row in `simulation.scope_levels` |
| Empty and start-less simulated windows were accepted | SIM-09 | DDL: `ck_simulation_runs_window` (non-empty, has a start; open end allowed) |
| Failed events need not carry a reason | SIM-10 | DDL: entering Failed needs `rva.change_reason` |
| A decommissioned asset's code could be reused | GT-10 | Asset lifecycle (GT-15): decommissioning keeps the row, so the code stays reserved |

New gaps follow the same path: record them as TODO tests, decide the owning layer, then fix test-first.
