# Verification Commands

Canonical build/test/lint commands for this repo. Any AI tool or human should use these rather than guessing.

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
