---
name: verify
description: Build, test, and lint commands for RVA (.NET solution, PostgreSQL schema and pgTAP, Python sidecar, container images, compose stack). Use before committing, before opening a pull request, when a step's acceptance criteria name a check, or whenever you need to know which command verifies a change.
---

# Verify

Canonical build, test, and lint commands for this repo. Any AI tool or human should use these rather than guessing. Tool wrappers (for example `.claude/skills/verify/SKILL.md`) point here; do not copy these commands into them.

## Commands

| Area | Command | Needs | Run it when |
|---|---|---|---|
| Setup, once per clone | `git config core.hooksPath .githooks` | — | Before the first commit |
| .NET build | `dotnet build VppAggregator.slnx` | .NET SDK | Any change under `src/` or `tests/`, or to MSBuild files |
| .NET tests, all | `dotnet test --solution VppAggregator.slnx` | .NET SDK; Docker for the integration tests | Any change under `src/` or `tests/`, or to MSBuild files |
| .NET tests, one project | `dotnet test --project tests/<Project>` | .NET SDK | Focused runs; projects without Docker |
| .NET tests, filtered | `dotnet test --project tests/<Project> --filter-class <FullName>` or `--filter-method "<pattern>"` | .NET SDK | Focused runs |
| Coverage | `dotnet test --solution VppAggregator.slnx --coverage --coverage-output-format cobertura` | .NET SDK, Docker | When coverage is asked for |
| Test framework guardrail | `dotnet test --project tests/Rva.ArchitectureTests --filter-class Rva.ArchitectureTests.TestFrameworkTests` | .NET SDK | Changes to `*.csproj`, `*.props`, `*.targets`, `global.json` (the pre-commit hook runs it) |
| Run the host | `dotnet run --project src/Rva.Backend/Rva.Host` | .NET SDK; the compose stack for `/health/ready` | Manual checks at `http://localhost:5000` |
| Schema lint and pgTAP | `infrastructure/postgres/tests/run_tests.sh` | Docker | Any change under `infrastructure/postgres/` |
| pgTAP, selected files | `infrastructure/postgres/tests/run_tests.sh pgtap/<file>.sql` | Docker | Focused schema runs |
| pgTAP with timing checks | `infrastructure/postgres/tests/run_tests.sh --perf` | Docker | When a step names a timing check |
| Development seed | `RVA_SEED_CONTAINER=rva_core_db infrastructure/postgres/seed/seed.sh` | The compose stack | Loading the sample topology locally |
| Python dev tools | `pip install -r requirements-dev.txt` (in `src/Rva.Intelligence`) | Python 3.12 | Before sidecar tests or lint |
| Python tests and lint | `pytest` and `ruff check .` | Python 3.12 | Not configured yet (see Python below) |
| Container images | `docker build -f src/Rva.Backend/Rva.Host/Dockerfile -t rva-backend .`, `docker build -f src/Rva.Simulator/Dockerfile -t rva-simulator .`, `docker build -t rva-intelligence src/Rva.Intelligence` | Docker | Changes to a Dockerfile or an image's inputs |
| Infrastructure | `docker compose -f infrastructure/docker/docker-compose.yml up -d` (add `--profile backend`, `simulator`, or `intelligence` with `--build`) | Docker | Running the stack locally |
| Harness references | `.agents/skills/verify/scripts/check-references.sh` | bash, git | Any change to `AGENTS.md`, `.ai/rules/`, `.agents/`, a tool adapter, or a file they name (moving or renaming one) |
| Contract changes | `.agents/skills/verify/scripts/check-contract-changes.sh --body-file <pr-description.md>` (optional `--base <ref>`, `--head <ref>`) | bash, git, perl | Before opening a pull request that changes a `.Contracts` project, an error code, or a route |

CI runs the same commands: `ci` (`.github/workflows/ci.yml`: .NET build and tests, images and health checks), `db-tests` (`run_tests.sh`), `test-guardrail`, and `harness` (`.github/workflows/harness.yml`: the harness checks below).

## Harness checks

`check-references.sh` (the `references` job in the `harness` workflow) fails a pull request when:

* a repo path named in `AGENTS.md`, `CLAUDE.md`, `.ai/rules/`, `.agents/`, `.claude/`, `.codex/`, `.cursor/rules/`, or `.github/*.md` does not exist. That covers backticked paths, `@`-references, and relative Markdown links. A backticked token counts as a path only if it starts with a top-level repo entry and holds no placeholder (`<Area>`), glob, or command syntax; build output (`/bin/`, `/obj/`) is skipped. Plans (`.ai/plans/`) are not checked, because they name files that past steps removed and future steps will add;
* a `.cursor/rules/*.mdc` body is anything other than one line starting `Follow @.ai/rules/<same name>.md, `, pointing at a rule that exists.

`check-contract-changes.sh` (the `contracts` job, pull requests only; it reruns when the description is edited) reports the pull request's public-surface changes (`.ai/rules/agent-workflow.md`, "Review items") in the job summary, and fails when a changed `.Contracts` file is not named:

* It lists every file changed under a `.Contracts` project, every error code added or removed there (a `public const string` whose value is `<Module>.<Code>`), and every route mapping (`MapGet`, `MapPost`, ...) added or removed under `src/`.
* Each listed Contracts file must appear in backticks, as its repo path or its file name, in the description's `## Review items` section. Text in HTML comments and in other sections does not count.
* In a test-first step, changes are measured from the `<plan-step-id>: tests first` commit, because the tests-first review covered the stubs; only what the implementer added must be named. Otherwise they are measured from the merge base.
* Naming a file records the item for review; it does not decide it. The decision goes in the plan's review-items table.

When it fails, the summary lists each Contracts file marked **no**. Add it to "Review items" (after the human has approved the addition), and the job reruns when the description is saved.

Write repo paths in full from the repo root (`infrastructure/postgres/tests/fixtures/`, never a path relative to some other folder), so the check and the reader resolve them the same way. When it fails, fix the reference or the file it points at in the same pull request; never weaken the check to make a change pass.

## Setup (per clone)

`git config core.hooksPath .githooks` enables the shared hooks in `.githooks/`, which block commits on `main` and pushes to `main`. The `pre-commit` hook also runs the test framework guardrail (`.ai/rules/testing.md`) when a commit touches MSBuild files or `global.json`. It needs `dotnet`, and adds a build of the architecture tests to those commits only. Changes reach `main` only through a pull request. The hooks are a local safety net that can be bypassed; for what is enforced where, see `.ai/rules/agent-workflow.md`, "Validation and Enforcement".

## C# (`src/Rva.Backend`, `src/Rva.Simulator`)

Tests run on xUnit v3 and Microsoft.Testing.Platform (MTP), switched on for the repo by `global.json` (`.ai/rules/testing.md`). Under MTP the solution is passed with `--solution`; the old `dotnet test VppAggregator.slnx` form is rejected. Coverage writes one report per test project to `TestResults/` (gitignored).

A test assembly that runs zero tests fails the run (MTP exit code 8). That keeps a full run from silently skipping a project, so never suppress it there. A filter across the whole solution matches nothing in some assemblies; for that case only, add `--ignore-exit-code 8`, or filter one `--project`.

VSTest options (`--filter "FullyQualifiedName~..."`, `--collect`, `--logger`) do not apply. The run summary lists each test assembly and a total.

`dotnet test` needs Docker for `tests/Rva.Modules.GridTopology.Integration.Tests`: it builds the pgTAP test image (`infrastructure/postgres/tests/Dockerfile`) and starts one throwaway PostgreSQL container per run through Testcontainers, with `infrastructure/postgres/core` mounted as in compose (and `infrastructure/postgres/seed`, for the seed tests); each test gets its own database copied from a template (schema only, or schema plus fixtures A-D). It never touches the compose stack. Without Docker, that project fails; run the others with `--project`. The unit tests (`tests/Rva.Modules.GridTopology.Tests`) need neither Docker nor a database. Timing checks are opt-in, because they measure speed rather than behavior and depend on the machine: set `RVA_PERF_TESTS=1` to run them (for example `GetConstraintPath` latency over the development seed, C-34); otherwise they are reported as skipped. Agents in a Claude Code sandbox may not be able to run `dotnet` (the test runner's local pipe) or reach Docker unless `/sandbox` allows them; their pull requests say so, and results come from CI or a local run.

`dotnet test` also includes `tests/Rva.ArchitectureTests`, which enforces the module boundaries in `.ai/rules/csharp-monolith.md` and the test framework standard in `.ai/rules/testing.md`. A failure there is a design violation to fix, not a test to update. It also includes `tests/Rva.Host.Tests`, which starts the real host in memory to check its wiring (deployment settings validation, integration event delivery).

Package versions are pinned once in the root `Directory.Packages.props`; `PackageReference` items carry no `Version` or `VersionOverride`.

### Running the host

`dotnet run --project src/Rva.Backend/Rva.Host` runs the Development environment (via `Properties/launchSettings.json`), listening on `http://localhost:5000`.

The host fails at startup, naming the key, if a required setting under `Rva:Deployment` or `ConnectionStrings` (`Core`, `Telemetry`, `Redis`), or `GridTopology:UtilityTimeZone` (an IANA id, set in `appsettings.json`), is missing or invalid. It starts without a database: `/health` is liveness only and never opens one, and `/health/ready` reports whether `rva_core` is reachable and has every lookup code the modules need (503 otherwise). `appsettings.Development.json` supplies local values for the compose stack. Its database connection strings carry no password: set `PGPASSWORD`, or override the whole string with `ConnectionStrings__Core` / `ConnectionStrings__Telemetry`.

## Database schema (`infrastructure/postgres`)

`run_tests.sh` builds both databases from their init scripts in throwaway containers (no named volumes, removed on exit), runs the DDL lint on each, then runs the pgTAP suite against `rva_core`. It needs Docker and nothing else, and does not touch the compose stack or its volumes.

It prints one TAP line per assertion and ends with `Result: PASS`; it exits non-zero if initialization, either lint, or any assertion fails. Lines marked `# TODO` are known schema gaps recorded as tests (see `docs/architecture/model-validation-scenarios.md`): they print `not ok` but do not fail the run. When a TODO test starts passing, pg_prove reports `TODO passed`; remove its `todo_start`/`todo_end` so it becomes enforcing.

* **DDL lint** (`infrastructure/postgres/tests/ddl_lint.sql`) checks the built schema against `.ai/rules/sql-ddl.md` (comments and their facts tags, naming, types, cross-schema foreign keys). Separately, the `rva_core` init fails if any table or column in `topology` or `simulation` has no comment.
* **pgTAP suite** (`infrastructure/postgres/tests/pgtap/`, one file per validation scenario, e.g. `gt05_local_overload.sql`) builds its fixtures from `infrastructure/postgres/tests/fixtures/` inside one transaction and rolls back. The test image (`infrastructure/postgres/tests/Dockerfile`) is the production PostGIS image plus pgTAP and pg_prove; `rva_core_init.sql` never installs pgTAP.

To run the lint or a single test file against the running compose stack instead (init scripts run only on an empty volume, so after changing one, recreate it with `docker compose -f infrastructure/docker/docker-compose.yml down -v`):

```
docker exec -i rva_core_db psql -U postgres -d rva_core -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/ddl_lint.sql
docker exec -i rva_telemetry_db psql -U postgres -d rva_telemetry -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/ddl_lint.sql
```

The pgTAP files need the pgTAP extension, which the compose image does not have, so run them through `run_tests.sh`.

### Development seed

The init script creates the schema and its lookup rows only. `seed.sh` loads the catalog (hardware profiles, protocol mappings) and the sample Portland topology (2 substations, 554 assets at all four connection levels, one microgrid, one overloaded transformer) into the running compose database.

Without `RVA_SEED_CONTAINER` it uses a local `psql` and the standard libpq variables (`PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD`, `PGDATABASE`, which defaults to `rva_core`). Both files run in one transaction, so a failure commits nothing. The script can be re-run: it adds no rows and no history. A catalog row that differs from `catalog.sql` is left unchanged and printed as a `catalog drift` WARNING. It exits 3 on a database without the `rva_core` schema. Tests never load the development topology (they use fixtures A–D); the seed's own tests in `tests/Rva.Modules.GridTopology.Integration.Tests/Seed/` run `seed.sh` against a throwaway database.

## Python (`src/Rva.Intelligence`)

Python 3.12. Runtime dependencies are in `requirements.txt`; test and lint tools in `requirements-dev.txt`.

No tests or lint configuration exist yet. Tests use pytest (`.ai/rules/testing.md`); once added, `pytest` and `ruff check .` are the expected commands.

PyTorch publishes no wheels for Intel macOS after 2.2, and llvmlite (needed by tslearn) has none either, so on an Intel Mac the sidecar runs only in its container (see Containers below).

## Containers

The `ci` workflow (`.github/workflows/ci.yml`) builds the three images and checks `/health` on the backend and sidecar. The .NET images build from the repo root; the sidecar builds from its own directory.

## Infrastructure

The application services in the compose file are behind profiles. Add `--profile backend` (or `simulator`, `intelligence`) to build and start them as well; the backend answers `http://localhost:5000/health`:

```
docker compose -f infrastructure/docker/docker-compose.yml --profile backend up -d --build
```
