# Plan p1-s00f: Test-driven validation of the rva_core DDL with pgTAP

**Step:** `p1-s00f` (see the step table in `PHASE_1_PLAN.md`) · **Status:** implemented, awaiting review · **Depends on:** `p1-s00e` (DDL lint and SQL standard; merged in PR #8)

## Context

`docs/architecture/model-validation-scenarios.md` lists design scenarios (GT-01..08, SIM-01..06) that are "not executable tests yet". The only executable check today is `infrastructure/postgres/tests/rva_core_smoke_test.sql`. It is one DO block with a hand-rolled `pg_temp.check()`, so it stops at the first failure, reports nothing per test, and can't be mapped back to scenario IDs. The goal is TDD for SQL: turn each scenario into executable assertions, run them continuously (locally and in CI), and let red tests expose gaps in the schema.

**Answers to the questions:**
- **Can you do TDD with SQL natively?** Yes. **pgTAP** is the PostgreSQL equivalent of xUnit. It has assertions like `throws_ok(sql, '23514')` (matched by SQLSTATE), `lives_ok`, `results_eq`, `is`, `has_table` and `col_is_pk`. **pg_prove** runs each test file in its own transaction and rolls it back, then reports pass/fail for every assertion. The workflow is the same as xUnit: write the failing test, change the DDL, and watch it go green.
- **Mocking:** In the DB you don't mock, you build fixtures. Fixtures A–D become reusable setup SQL inside the rolled-back transaction. The scenarios that need mocking are the ones owned by the GridTopology domain (cross-feeder microgrid membership, non-FTM above the meter, asset-in-scope, scope_root_code resolution, telemetry availability). There is deliberately no cross-module FK, so SQL can't check these. They belong in xUnit + Testcontainers with a fake `IGridTopologyQueries` once the C# modules exist. This plan records them as pending, routed by the "owner" column. It does not test them in SQL.
- **Layering:** `ddl_lint.sql` stays a separate gate for structure and comments. pgTAP covers the DB-owned behaviour. xUnit covers domain rules later.

## Step 1: Test image with pgTAP (test-only, never in the production init)

- Confirmed: PGDG ships `postgresql-18-pgtap` (1.3.4) and `libtap-parser-sourcehandler-pgtap-perl` (3.37) for the `postgis/postgis:18-3.6` base, so no source build is needed.
- Add `infrastructure/postgres/tests/Dockerfile`: `FROM postgis/postgis:18-3.6`, install `postgresql-18-pgtap` and `libtap-parser-sourcehandler-pgtap-perl` (which provides `pg_prove`).
- Do **not** add `CREATE EXTENSION pgtap` to `rva_core_init.sql`. Mount `infrastructure/postgres/core` over `/docker-entrypoint-initdb.d` exactly as compose does, so the image's postgis_topology init is still suppressed. Each test file runs `CREATE EXTENSION IF NOT EXISTS pgtap;` at its top, inside the transaction, so it rolls back as well.

## Step 2: Test layout

```
infrastructure/postgres/tests/
  Dockerfile
  run_tests.sh              # build image, start throwaway container, wait for init, run ddl_lint + pg_prove, stop
  ddl_lint.sql              # unchanged
  fixtures/
    _helpers.sql            # set rva.actor/actor_type, lookup-id helper functions (pg_temp)
    fixture_a_hybrid_home.sql
    fixture_b_nonexport_warehouse.sql
    fixture_c_large_residential.sql
    fixture_d_community_microgrid.sql
  pgtap/
    gt01_hierarchy_identity.sql
    gt02_shared_inverter.sql
    ... one file per scenario ID (gt03–gt08, sim01–sim06, new ones below)
```

- Each test file follows this pattern: `BEGIN; CREATE EXTENSION IF NOT EXISTS pgtap; \ir ../fixtures/...; SELECT plan(n); ...assertions...; SELECT * FROM finish(); ROLLBACK;`
- The test descriptions start with the scenario ID, e.g. `'GT-04: premise export cap is 10 kW'`, so the TAP output maps straight back to the doc.
- Fixtures use synthetic codes such as `FXA-PREM` and live only in `tests/`. They never go in the dev seed, as the scenario doc requires.
- Clock: `as_of` tests capture `clock_timestamp()` between writes, as the smoke test already does. Time can't be frozen; tests rely only on order.

## Step 3: Migrate the smoke test

Port every check in `rva_core_smoke_test.sql` into the matching pgTAP files: versioning goes to gt08, grid node identity to gt01, constraints to gt02/gt06, simulation triggers to sim03/sim04, and add-column versioning to a `versioning_add_column.sql`. For example, the `BEGIN ... EXCEPTION WHEN check_violation` blocks become `throws_ok($$INSERT ...$$, '23514', NULL, 'GT-06: two connection points rejected')`. Delete the smoke test once its checks all exist in pgTAP, and update `verify.md`.

## Step 4: Write scenario tests, expecting some to fail

Only DB-owned expectations are written as pgTAP tests. Domain-owned ones are listed in a `-- PENDING (domain)` header in each file and in the doc's owner column. These existing scenarios will fail against the current DDL. That is intended, and each one needs your decision: **fix the DDL, or reassign the rule to the domain layer**. Don't weaken a test just to make it pass.

| Gap | Evidence |
|---|---|
| SIM-04 "scope… cannot drift": `scope_level_id`, `scope_root_code`, `detail_level_id`, `simulated_window`, and `configuration_json` stay mutable after Pending | `simulation_runs_bookkeeping` freezes only `run_kind_id`, `topology_as_of`, and `initial_state_as_of` (rva_core_init.sql:853-858) |
| SIM-04 "invalid lifecycle transitions are rejected": nothing stops Completed → Running or Failed → Pending | no transition check exists |
| SIM-04: `started_at`/`ended_at` are not tied to status. A Pending run can have `started_at` set, and a Completed run can have no `ended_at` | only the ordering CHECKs at :774-775 |
| SIM-05: an empty `simulated_window` is accepted | no `NOT isempty()` CHECK |
| SIM-01 / Fixture D: a run can't be scoped to a microgrid | `simulation.scope_levels` has no `microgrid` row, but `grid_node_types` does |

## Step 5: Add scenarios to the doc

Add these to `model-validation-scenarios.md`. Each states the schema fact it probes and the layer that owns it.

**Topology**
- **GT-09 Load transfer / reparenting**: move a premise to another transformer or feeder. Snapshots before and after resolve different constraint paths, and the history row records the old parent. This is the most common real DERMS topology change. (DB: versioning; domain: path)
- **GT-10 Decommission and code reuse**: delete an asset, then decide whether its `asset_code` may be reused. The UNIQUE is only on current rows, so a reused code would make history ambiguous for anyone looking up by code. (Decision needed)
- **GT-11 Sign conventions by technology**: PV-only (`p_min = 0`), EVSE as a managed load (`p_max = 0`), V2G EVSE (bidirectional), and a battery. All must pass the bounds and kVA CHECKs, and the flexible-load case must not be confused with "no capability". (DB)
- **GT-12 Nested limits**: an asset `export_limit_kw` above its premise cap, and a premise under a microgrid with PCC export = 0. The schema allows both; the effective limit is the minimum along the path. (DB accepts; domain computes)
- **GT-13 Microgrid PCC flags**: a microgrid with `can_island = false`, import limit 0, or export limit 0. Check that 0 still means "zero", not NULL/unknown. (DB)
- **GT-14 Fleet-scale snapshot**: a generated subtree of 500+ assets through the `_as_of` functions completes within a time budget. A rough guard against gross slowdowns in the `_as_of` path at the 500-DER scale. At 600 rows it will not catch subtle regressions such as a lost index. (DB perf; separate, non-blocking file)

**Simulation**
- **SIM-07 As-of override validity**: an override references an asset that exists at `topology_as_of` but was deleted afterwards. Validation must resolve as-of, not against current rows. (Domain; pending)
- **SIM-08 Microgrid-scoped run**: an islanding scenario for Fixture D needs a microgrid scope (see the Step 4 gap). (DB lookup + domain)
- **SIM-09 Window edges**: an empty range, a half-open range, a window spanning a DST transition (America/Los_Angeles), and an unbounded upper end. Pin down what `simulated_window` accepts. (DB)
- **SIM-10 Failure reasons and actor**: Running → Failed without `rva.change_reason` set. Decide whether the event must carry a reason; an audit trail of failures without causes is weak for NERC CIP. (DB or domain decision)
- **SIM-11 Reproducibility pair**: two runs with identical inputs created at different times, with a topology edit between them. When `topology_as_of` is pinned to the earlier time, both resolve the same snapshot. (DB as_of)

## Step 6: Continuous runs

- `infrastructure/postgres/tests/run_tests.sh` builds the test image, starts a `--rm` container, waits for `PostgreSQL init process complete`, then runs `ddl_lint.sql` (core) and `pg_prove -d rva_core -r /tests/pgtap`, then stops the container. It reuses the throwaway flow already documented in `verify.md`.
- Add `.github/workflows/db-tests.yml` (there is no CI yet). It triggers on changes under `infrastructure/postgres/**` and `docs/architecture/model-validation-scenarios.md`, and runs `run_tests.sh`. It also runs the telemetry `ddl_lint`.
- Update `.ai/scripts/verify.md` (the commands) and `.ai/rules/sql-ddl.md` (one line: every DDL change ships with a pgTAP test, and SQLSTATE assertions are preferred over message matching).

### Known gaps as pgTAP TODO tests

Known gaps go between `todo_start('<gap and decision needed>')` and `todo_end()`. They print `not ok ... # TODO` with the reason, but they don't fail the run, so CI enforces everything else from day one. When a gap is fixed, pg_prove reports `TODO passed`, and the markers are removed. Rules owned by the domain get a passing `boundary:` test, which pins that the DB accepts the input; moving such a rule into the DDL is then a visible change.

## Scope extension: asset lifecycle, participation, and grid state (approved 2026-10-17)

Writing GT-10 showed the schema could not tell a decommission from a deleted mistake, and that "the row exists" quietly meant commissioned and active. The extension was done test-first (GT-15 to GT-18 red, then the DDL):

- **Lifecycle** on `topology.assets`: `asset_lifecycle_state_id` (planned, installed, commissioned, decommissioned; default planned) and the field dates `in_service_date` and `decommissioned_date`. `topology.check_asset_lifecycle` keeps states and dates consistent and makes decommissioned terminal. It also limits `DELETE` to corrections: decommissioned assets can't be deleted, and any delete needs a change reason.
- **Participation:** `participation_state_id` (not_enrolled, active, suspended, opted_out; default not_enrolled). Only commissioned assets can be active or suspended. Multi-program enrollment is a later decision.
- **Grid state:** the `topology.grid_states` lookup (connected_energized, connected_deenergized, islanded, disconnected, each flagged as grid-connected or not and energized or not), plus `topology.grid_state_events`. The events are append-only, target a grid node or an asset, and carry field and recorded times. `topology.latest_grid_states(t)` returns each target's latest state as known at t. Deriving an asset's effective state is GridTopology's job; `pg_temp.asset_grid_states` in the test helpers is its executable spec.
- Real-time availability (online, maintenance) stays operational state from telemetry and Redis; it is not part of the registry.

## Critical files

- New: `infrastructure/postgres/tests/{Dockerfile,run_tests.sh,fixtures/*,pgtap/*}`, `.github/workflows/db-tests.yml`
- Modified: `docs/architecture/model-validation-scenarios.md` (owner column, new scenarios, "executable in pgtap/<file>"), `.ai/scripts/verify.md`, `.ai/rules/sql-ddl.md`
- Removed after porting: `infrastructure/postgres/tests/rva_core_smoke_test.sql`
- `rva_core_init.sql`: modified only for the scope extension below. The Step 4 gaps are fixed afterwards, one at a time and test-first, once you decide which layer owns each.

## Verification

1. `./infrastructure/postgres/tests/run_tests.sh` prints TAP output with one line per assertion. `ddl_lint: PASS`.
2. Every check from the old smoke test passes in pgTAP: compare its list of checks against the ported assertions.
3. The Step 4 gaps show as `not ok` and name their scenario IDs. That confirms the tests catch real gaps. Then fix one gap, for example by freezing scope in `simulation_runs_bookkeeping`, and watch its test turn green.
4. Break a constraint on purpose (drop `ck_assets_single_connection` locally). The GT-06 test should fail and the run should exit non-zero.
5. Push the branch and confirm the GitHub Actions job runs and passes or fails the same way as locally.

## Outcome

- 29 pgTAP files (GT-01..13, GT-15..18, SIM-01..11, versioning_add_column) plus `perf/gt14_fleet_snapshot.sql`, with 236 assertions, all passing and none left as TODO. All seven decisions from the first run are resolved (listed in `docs/architecture/model-validation-scenarios.md`). Code reuse (GT-10) was settled by the lifecycle extension. The six simulation gaps were fixed test-first in the DDL: scope frozen at creation, window and configuration frozen after Pending, transition and timestamp rules plus a required failure reason (`simulation.check_run_lifecycle`), a microgrid scope level, and a non-empty window check.
- Every check from the former `rva_core_smoke_test.sql` is ported, and the smoke test is removed.
- Mutation checks: dropping `ck_assets_single_connection` fails GT-01 (tests 14–15) and GT-06 (tests 5–6). Dropping `trg_assets_lifecycle`, the grid state events' append-only trigger, and their single-target CHECK fails GT-15, GT-16, and GT-17.
- Lessons now recorded as rules in `.ai/rules/sql-ddl.md` section 7. A rejection test can pass for the wrong reason: Completed → Running first "passed" because an unrelated timestamp CHECK fired. A volatile function in a `WHERE` clause runs once per scanned row.
- Next: the domain-owned (pending) expectations become xUnit tests when the GridTopology and Simulation modules are built. New gaps follow the same path: TODO test, decide the owning layer, then fix test-first.
