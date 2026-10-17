# Verification Commands

Canonical build/test/lint commands for this repo. Any AI tool or human should use these rather than guessing.

## One-time setup (per clone)

```
git config core.hooksPath .githooks
```

Enables the shared hooks in `.githooks/`, which block commits on `main` and pushes to `main`. Changes reach `main` only through a pull request (see `.ai/rules/agent-workflow.md`). The hooks are a local safety net; branch protection on GitHub is the real enforcement.

## C# (`src/Rva.Backend`, `src/Rva.Simulator`)

```
dotnet build VppAggregator.slnx
dotnet test VppAggregator.slnx
```

`dotnet test` includes `tests/Rva.ArchitectureTests`, which enforces the module boundaries in `.ai/rules/csharp-monolith.md`. A failure there is a design violation to fix, not a test to update.

## Database schema (`infrastructure/postgres`)

One command builds both databases from their init scripts in throwaway containers (no named volumes, removed on exit), runs the DDL lint on each, then runs the pgTAP suite against `rva_core`. It needs Docker and nothing else, and does not touch the compose stack or its volumes:

```
infrastructure/postgres/tests/run_tests.sh                                   # lint + every pgTAP test
infrastructure/postgres/tests/run_tests.sh pgtap/gt05_local_overload.sql     # lint + only these test files
infrastructure/postgres/tests/run_tests.sh --perf                            # also the timing checks in perf/
```

It prints one TAP line per assertion and ends with `Result: PASS`; it exits non-zero if initialization, either lint, or any assertion fails. Lines marked `# TODO` are known schema gaps recorded as tests (see `docs/architecture/model-validation-scenarios.md`): they print `not ok` but do not fail the run. When a TODO test starts passing, pg_prove reports `TODO passed`; remove its `todo_start`/`todo_end` so it becomes enforcing.

* **DDL lint** (`tests/ddl_lint.sql`) checks the built schema against `.ai/rules/sql-ddl.md` (comments and their facts tags, naming, types, cross-schema foreign keys). Separately, the `rva_core` init fails if any table or column in `topology` or `simulation` has no comment.
* **pgTAP suite** (`tests/pgtap/`, one file per validation scenario, e.g. `gt05_local_overload.sql`) builds its fixtures from `tests/fixtures/` inside one transaction and rolls back. The test image (`tests/Dockerfile`) is the production PostGIS image plus pgTAP and pg_prove; `rva_core_init.sql` never installs pgTAP.

To run the lint or a single test file against the running compose stack instead (init scripts run only on an empty volume, so after changing one, recreate it with `docker compose -f infrastructure/docker/docker-compose.yml down -v`):

```
docker exec -i rva_core_db psql -U postgres -d rva_core -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/ddl_lint.sql
docker exec -i rva_telemetry_db psql -U postgres -d rva_telemetry -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/ddl_lint.sql
```

The pgTAP files need the pgTAP extension, which the compose image does not have, so run them through `run_tests.sh`.

## Python (`src/Rva.Intelligence`)

No test/lint tooling configured yet. Once added, expected commands:

```
pytest
ruff check .
```

## Infrastructure

```
docker compose -f infrastructure/docker/docker-compose.yml up -d
```
