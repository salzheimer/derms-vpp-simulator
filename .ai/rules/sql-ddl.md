# SQL DDL Standard

Rules for writing or changing PostgreSQL DDL in this repo (`infrastructure/postgres/**`, and later migrations), by humans or agents. The DDL is the source of truth for the data model; these rules keep it self-describing and consistent. Rules marked **[lint]** are checked by `infrastructure/postgres/tests/ddl_lint.sql` (see `.agents/skills/verify/SKILL.md`); the rest are checked in review.

## 1. Comments

Every table and every column has a `COMMENT`. Comments are what tools, the ERD, EF Core scaffolding, and agents querying the catalog see, so they must stand on their own.

**Table comment [lint]:** what one row is, why the table exists, and anything a reader must know to use it correctly (ownership, append-only, how rows are created). Plain prose.

**Column comment [lint]:** a purpose followed by a facts tag in square brackets at the end:

```sql
COMMENT ON COLUMN topology.feeders.substation_id IS
    'Substation that normally serves this feeder (as-designed configuration, not the energized path). [BIGINT; NOT NULL; FK topology.substations(substation_id)]';
```

* **Purpose:** what the value means, its unit, and what special values mean (e.g. `NULL = no limit`, `0 = non-export`, negative = charging). Explain *why* a constraint exists when it is not obvious. No square brackets.
* **Facts tag:** items separated by `;`. No `[`, `]`, or `;` inside an item. The first item is the type; the rest may be in any order but the order below is preferred. The lint compares every item with the catalog, so the tag cannot drift from the DDL.

| Item | When | Checked against |
|---|---|---|
| `<TYPE>` | always, first | the column type, written as in the DDL: `BIGINT`, `INTEGER`, `VARCHAR(50)`, `DOUBLE PRECISION`, `TIMESTAMPTZ`, `TSTZRANGE`, `JSONB`, `UUID`, `BOOLEAN`, `GEOGRAPHY(Point, 4326)`, ... (case and spaces are ignored) |
| `NOT NULL` or `NULL` | always, exactly one | the column's nullability |
| `IDENTITY` | exactly when the column is an identity column | `GENERATED ALWAYS AS IDENTITY` |
| `DEFAULT <description>` | exactly when the column has a default (identity excluded) | presence only; the description is free text, e.g. `DEFAULT clock_timestamp()` |
| `GENERATED <description>` | exactly when the column is a generated column | presence only |
| `PK` | exactly when the column is in the primary key | the primary key |
| `UNIQUE` / `UNIQUE (<col>, <col>)` | one item per unique constraint or unique index on the column (primary key excluded); single-column ones are bare `UNIQUE`, composite ones list all their columns in key order | the unique constraints and indexes |
| `FK <schema>.<table>(<col>[, <col>])[ ON DELETE CASCADE]` | one item per foreign key the column is part of; composite keys list all referenced columns, and both local columns carry the same item | the foreign keys, including `ON DELETE CASCADE` |
| `REF <target and how it is validated>` | **required** for an `_id` column that is not a primary key, foreign key, single-column unique, or identity column (being part of a composite unique key does not exempt it); recommended for any other plain-value reference (codes, actors) | presence only |
| `CHECK <description>` | exactly when the column takes part in a `CHECK` constraint (including multi-column ones) | presence only; describe the rule in words, e.g. `CHECK > 0`, `CHECK exactly one of the connection columns is set` |

`REF` is how a column documents a reference that deliberately has no foreign key, such as a cross-module ID (modules never share foreign keys, see section 4) or an actor ID owned by the later Identity module:

```sql
COMMENT ON COLUMN simulation.simulation_asset_states.asset_id IS
    'Asset whose starting state this row overrides. [BIGINT; NOT NULL; UNIQUE (simulation_run_id, asset_id); REF topology.assets(asset_id), validated through IGridTopologyQueries (no cross-module FK)]';
```

**Generated comments:** `topology.enable_versioning(<table>)` writes the comments on `created_at`, `sys_period`, and every column of the `<table>_history` twin (copying each main column's purpose). Do not write those by hand; write the main table's column comments first, then call `enable_versioning`. After changing a main column's comment, re-run `enable_versioning` for that table so its history comments follow.

Inline `--` comments in DDL are for implementation notes (why a trigger is ordered a certain way, etc.), not for documenting what a column means. Put meaning in the `COMMENT`.

## 2. Naming

* Lowercase `snake_case` for schemas, tables, columns, constraints, indexes, triggers, and functions **[lint]**. Tables are plural nouns.
* Primary keys `{entity}_id`, natural codes `{entity}_code`, and a foreign key column uses the referenced primary key's exact name (`PHASE_1_PLAN.md`, Cross-cutting decisions). A second reference to the same table gets a role prefix (e.g. `control_hardware_profile_id`).
* Explicitly named constraints use a prefix **[lint]**: `pk_`, `uk_` (unique), `ck_` (check), `fk_`, `ex_` (exclusion), followed by the table name and a short description (`ck_premises_single_connection`). Inline single-column constraints may keep PostgreSQL's generated name (`<table>_<col>_check`, `_fkey`, `_key`, `_pkey`).
* Indexes not created by a constraint start with `ix_` (or `ux_` for a unique index) **[lint]**: `ix_<table>_<columns>`.
* Triggers start with `trg_` **[lint]**: `trg_<table>_<purpose>`.
* Units go in the column name: `_kw`, `_kwh`, `_kva`, `_kv`, `_v`, `_pct`.

## 3. Types and defaults

* Entity keys `BIGINT GENERATED ALWAYS AS IDENTITY`; lookup keys `INTEGER GENERATED ALWAYS AS IDENTITY`; time-ordered public IDs `UUID DEFAULT uuidv7()`. No `serial` **[lint]**.
* Timestamps are `TIMESTAMPTZ`, never `TIMESTAMP` **[lint]**. Defaults and triggers use `clock_timestamp()`, not `now()` or `CURRENT_TIMESTAMP` **[lint for defaults]**, so several changes in one transaction stay ordered.
* `JSONB`, never `JSON` **[lint]**; `VARCHAR(n)` or `TEXT`, never `CHAR(n)` **[lint]**; no `MONEY` **[lint]**.
* Constrained values are lookup tables with an FK, not `ENUM` types **[lint]**. Lookups have `internal_code VARCHAR(50) NOT NULL UNIQUE` and `display_name`.
* Physical quantities are `DOUBLE PRECISION` with a `CHECK` on their valid range. Geometry uses SRID 4326: `GEOGRAPHY(Point, 4326)` for locations, `GEOMETRY(LineString, 4326)` for lines.

## 4. Keys, constraints, and module boundaries

* Each module owns one schema; nothing of ours goes in `public` **[lint]** (it holds extension objects only, and the lint does not check it otherwise). **No foreign key crosses schemas [lint].** Reference another module's data by plain ID or code, document it with `REF`, and validate it through the owning module's `.Contracts` (`.ai/rules/csharp-monolith.md`).
* Every table has a primary key **[lint]** (history tables excepted). Entities also carry a natural `_code` with a unique constraint, for idempotent seeding.
* Enforce invariants in the database: `NOT NULL`, `CHECK` for ranges and "exactly one of" rules (`num_nonnulls(...) = 1`), unique constraints for natural keys, composite FKs for cross-column consistency.
* Index every foreign key column on an entity table that is queried or deleted through it.

## 5. Change tracking

Follow `.ai/rules/csharp-monolith.md` section 9: edited state is system-versioned (`topology.enable_versioning`, re-run after any `ALTER TABLE` in the same script), things that happen are append-only events rejected on `UPDATE`/`DELETE` by trigger, and who/why come from `SET LOCAL rva.*`. A new versioned table must be added to the `enable_versioning` list and to the self-check at the end of the init script.

## 6. Scripts

* Scripts are re-runnable: `CREATE ... IF NOT EXISTS`, `CREATE OR REPLACE` for functions and triggers, `INSERT ... ON CONFLICT (<natural key>) DO NOTHING` for seed rows. Exception: a table with a `BEFORE INSERT` trigger that writes elsewhere (the grid node tables, whose trigger registers a `topology.grid_nodes` row) is seeded with `INSERT ... SELECT ... WHERE NOT EXISTS (<natural key>)`. The trigger fires before `ON CONFLICT` is checked, so `DO NOTHING` would leave an orphan row on every re-run.
* Keep each schema's objects in dependency order: lookups, tables, comments, triggers, versioning, seeds, outbox, self-check.
* The init scripts only run on an empty database. Changing a deployed schema needs a migration tool (`PHASE_1_PLAN.md`, Digital twin readiness); the same rules apply to migrations.
* When the schema changes, update `docs/architecture/data-model.md` (the ERDs) in the same pull request.

## 7. Tests

Behavior the database owns (constraints, triggers, versioning, `_as_of` reads, append-only rules) is tested with pgTAP in `infrastructure/postgres/tests/pgtap/`, run by `run_tests.sh` (`.agents/skills/verify/SKILL.md`). Write the test first and watch it fail, then change the DDL.

* One file per validation scenario in `docs/architecture/model-validation-scenarios.md`, named after its ID (`gt05_local_overload.sql`); every assertion description starts with the ID (`'GT-05: ...'`) so failures map back to the scenario.
* Build data from the shared fixtures in `tests/fixtures/` (A–D, synthetic codes `FXA-`…`FXD-`), never from the development seed. Each file runs in one transaction and ends with `ROLLBACK`.
* Assert rejections by SQLSTATE (`throws_ok(sql, '23514', NULL, ...)`), not by message text. Make sure the error comes from the rule under test: an unrelated constraint can make a rejection test pass for the wrong reason.
* A rule the scenario expects but the schema does not yet enforce goes between `todo_start('<gap and decision needed>')` and `todo_end()`, never weakened to pass. A TODO test may accept any error (`NULL` SQLSTATE) while the mechanism is undecided; when the DDL is fixed, remove the markers and pin the SQLSTATE the fix raises.
* A rule owned by a backend module (cross-module references, rules spanning the hierarchy) is not enforced in SQL. Pin the boundary with a `lives_ok` test labelled `boundary:` and test the rule in the module.
* Do not call a volatile function (e.g. one that inserts) in a `WHERE` clause; it runs once per scanned row. Create the row in its own statement first.

## 8. Before you open a pull request

1. Every new table and column has a comment in the format above (and history comments regenerated via `enable_versioning`).
2. Run `infrastructure/postgres/tests/run_tests.sh` (`.agents/skills/verify/SKILL.md`): it recreates both databases from their init scripts, runs the lint, and runs the pgTAP suite. The init itself fails if any table or column in `topology` or `simulation` has no comment. A schema change ships with the pgTAP tests that cover it (section 7).
3. A lint failure is a schema or comment defect to fix. Do not weaken the lint to make a change pass; if a rule is wrong, change this document and the lint together in their own pull request.
