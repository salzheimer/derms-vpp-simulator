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
