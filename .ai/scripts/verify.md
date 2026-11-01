# Verification Commands

Canonical build/test/lint commands for this repo. Any AI tool or human should use these rather than guessing.

## One-time setup (per clone)

```
git config core.hooksPath .githooks
```

Enables the shared hooks in `.githooks/`, which block commits on `main` and pushes to `main`. The `pre-commit` hook also runs the test framework guardrail (`.ai/rules/testing.md`) when a commit touches MSBuild files or `global.json`. It needs `dotnet`, and adds a build of the architecture tests to those commits only. Changes reach `main` only through a pull request (see `.ai/rules/agent-workflow.md`). The hooks are a local safety net; branch protection on GitHub is the real enforcement.

## C# (`src/Rva.Backend`, `src/Rva.Simulator`)

```
dotnet build VppAggregator.slnx
dotnet test --solution VppAggregator.slnx
```

Tests run on xUnit v3 and Microsoft.Testing.Platform (MTP), switched on for the repo by `global.json` (`.ai/rules/testing.md`). Under MTP the solution is passed with `--solution`; the old `dotnet test VppAggregator.slnx` form is rejected. Other MTP forms:

```
dotnet test --project tests/Rva.Host.Tests                                                # one test project
dotnet test --project tests/Rva.Host.Tests --filter-class Rva.Host.Tests.IntegrationEventTests
dotnet test --project tests/Rva.Host.Tests --filter-method "*Host_starts*"
dotnet test --solution VppAggregator.slnx --coverage --coverage-output-format cobertura  # one report per test project, in TestResults/ (gitignored)
```

A test assembly that runs zero tests fails the run (MTP exit code 8). That keeps a full run from silently skipping a project, so never suppress it there. A filter across the whole solution matches nothing in some assemblies; for that case only, add `--ignore-exit-code 8`, or filter one `--project` as above.

VSTest options (`--filter "FullyQualifiedName~..."`, `--collect`, `--logger`) do not apply. The run summary lists each test assembly and a total.

`dotnet test` needs Docker for `tests/Rva.Modules.GridTopology.Integration.Tests`: it builds the pgTAP test image (`infrastructure/postgres/tests/Dockerfile`) and starts one throwaway PostgreSQL container per run through Testcontainers, with `infrastructure/postgres/core` mounted as in compose; each test gets its own database copied from a template (schema only, or schema plus fixtures A-D). It never touches the compose stack. Without Docker, that project fails; run the others with `--project`. The unit tests (`tests/Rva.Modules.GridTopology.Tests`) need neither Docker nor a database. Agents in a Claude Code sandbox may not be able to run `dotnet` (the test runner's local pipe) or reach Docker unless `/sandbox` allows them; their pull requests say so, and results come from CI or a local run.

`dotnet test` also includes `tests/Rva.ArchitectureTests`, which enforces the module boundaries in `.ai/rules/csharp-monolith.md` and the test framework standard in `.ai/rules/testing.md`. A failure there is a design violation to fix, not a test to update. It also includes `tests/Rva.Host.Tests`, which starts the real host in memory to check its wiring (deployment settings validation, integration event delivery).

Package versions are pinned once in the root `Directory.Packages.props`; `PackageReference` items carry no `Version` or `VersionOverride`.

To run the host locally (Development environment, via `Properties/launchSettings.json`, listening on `http://localhost:5000`):

```
dotnet run --project src/Rva.Backend/Rva.Host
```

The host fails at startup, naming the key, if a required setting under `Rva:Deployment` or `ConnectionStrings` (`Core`, `Telemetry`, `Redis`), or `GridTopology:UtilityTimeZone` (an IANA id, set in `appsettings.json`), is missing or invalid. It starts without a database: `/health` is liveness only and never opens one, and `/health/ready` reports whether `rva_core` is reachable and has every lookup code the modules need (503 otherwise). `appsettings.Development.json` supplies local values for the compose stack. Its database connection strings carry no password: set `PGPASSWORD`, or override the whole string with `ConnectionStrings__Core` / `ConnectionStrings__Telemetry`.

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

Python 3.12. Runtime dependencies are in `requirements.txt`; test and lint tools in `requirements-dev.txt`:

```
pip install -r requirements-dev.txt
```

No tests or lint configuration exist yet. Tests use pytest (`.ai/rules/testing.md`). Once added, expected commands:

```
pytest
ruff check .
```

PyTorch publishes no wheels for Intel macOS after 2.2, and llvmlite (needed by tslearn) has none either, so on an Intel Mac the sidecar runs only in its container (see Containers below).

## Containers

The `ci` workflow (`.github/workflows/ci.yml`) builds these images and checks `/health` on the backend and sidecar. The .NET images build from the repo root; the sidecar builds from its own directory:

```
docker build -f src/Rva.Backend/Rva.Host/Dockerfile -t rva-backend .
docker build -f src/Rva.Simulator/Dockerfile -t rva-simulator .
docker build -t rva-intelligence src/Rva.Intelligence
```

## Infrastructure

```
docker compose -f infrastructure/docker/docker-compose.yml up -d
```

The application services are behind profiles. Add `--profile backend` (or `simulator`, `intelligence`) to build and start them as well; the backend answers `http://localhost:5000/health`:

```
docker compose -f infrastructure/docker/docker-compose.yml --profile backend up -d --build
```
