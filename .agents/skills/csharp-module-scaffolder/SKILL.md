---
name: csharp-module-scaffolder
description: Scaffold a new backend module (Rva.Modules.<Area> + Rva.Modules.<Area>.Contracts projects, vertically sliced) in src/Rva.Backend, following .ai/rules/csharp-monolith.md.
---

# C# Module Scaffolder

Canonical, platform-agnostic definition. Tool-specific adapters (e.g. `.claude/skills/csharp-module-scaffolder/SKILL.md`) should reference this file rather than duplicating it.

## When to use

When adding a new module (bounded context) under `src/Rva.Backend/Modules/`, e.g. `Dispatch` or `Audit`. Read `.ai/rules/csharp-monolith.md` first; this skill applies it and does not override it.

## Steps

1. **Create two class library projects** under `src/Rva.Backend/Modules/`:
   - `Rva.Modules.<Area>.Contracts` — references `Rva.SharedKernel` only.
   - `Rva.Modules.<Area>` — references `Rva.SharedKernel`, its own `.Contracts`, and (only if needed) other modules' `.Contracts`. Never another module's implementation project.
2. **Lay out the implementation project by feature, not by layer:**
   ```text
   Rva.Modules.<Area>/
   ├── Features/<FeatureName>/   <-- endpoint, command/query, handler, validator, DTOs
   ├── Domain/                   <-- module-internal entities and value objects
   ├── Persistence/              <-- <Area>DbContext + Fluent API configurations (own schema)
   └── <Area>Module.cs           <-- public Add<Area>Module() and Map<Area>Endpoints()
   ```
   Do not create `Controllers/`, `Services/`, or `Repositories/` folders.
3. **Make everything `internal`** except `<Area>Module.cs`.
4. **Define the public surface in `.Contracts`:** query interfaces (`I<Area>Queries`), immutable snapshot records, and integration events implementing `IIntegrationEvent`. Use `Rva.SharedKernel` IDs and units in their signatures.
5. **Give the module its own `DbContext`** mapped to its own schema. Do not add foreign keys to other modules' tables; store their IDs as plain columns.
6. **Wire it into the host:** reference both projects from `Rva.Host` and call `Add<Area>Module()` / `Map<Area>Endpoints()` in `Program.cs`. Add both projects to `VppAggregator.slnx`.
7. **Register the module with the architecture tests** in `tests/Rva.ArchitectureTests` so the boundary rules cover it.

## Verification

Run `dotnet build VppAggregator.slnx` and `dotnet test VppAggregator.slnx` (see `.ai/scripts/verify.md`). The architecture tests must pass with the new module included.
