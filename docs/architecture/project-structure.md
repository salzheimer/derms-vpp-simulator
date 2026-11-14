# Project Structure

Where everything lives. The backend is a modular monolith built from separate `.csproj` projects, so module boundaries are enforced at compile time. The simulator and the AI sidecar are separate processes, not backend modules.

```text
vpp-aggregator/
├── AGENTS.md                        canonical AI-agent instructions (a map that links to .ai/)
├── CLAUDE.md                        thin pointer to AGENTS.md for Claude Code
├── README.md                        human-facing overview
├── VppAggregator.slnx               .NET solution
├── .ai/
│   ├── rules/                       agent-workflow, csharp-monolith, sql-ddl, testing, python-sidecar, security, domain-glossary
│   └── plans/                       MASTER_PLAN, PHASE_1_PLAN, step plans, AI_HARNESS_PLAN
├── .agents/
│   ├── agents/                      canonical agent definitions (test-author, implementer)
│   └── skills/                      canonical agent skills (verify: build, test, and lint commands; csharp-module-scaffolder)
├── .claude/, .codex/                thin wrappers that point to .agents/
├── .cursor/rules/                   thin wrappers that point to .ai/rules/
├── .github/copilot-instructions.md  pointer to AGENTS.md and .ai/rules/
├── .githooks/                       pre-commit / pre-push: block direct changes to main
├── docs/architecture/               these diagrams
│
├── src/
│   ├── Rva.Backend/
│   │   ├── Rva.Host/                composition root: DI, Serilog, middleware, endpoints, background workers
│   │   ├── Rva.SharedKernel/        typed IDs, units, reference codes, Result; grows on demand
│   │   └── Modules/
│   │       ├── Rva.Modules.GridTopology/
│   │       ├── Rva.Modules.GridTopology.Contracts/
│   │       ├── Rva.Modules.Telemetry/
│   │       ├── Rva.Modules.Telemetry.Contracts/
│   │       ├── Rva.Modules.Simulation/
│   │       └── Rva.Modules.Simulation.Contracts/
│   ├── Rva.Simulator/               separate process: Chaos Engine device fleet
│   └── Rva.Intelligence/            separate process: Python AI sidecar (recommendations only)
│
├── tests/
│   └── Rva.ArchitectureTests/       ArchUnitNET suite enforcing the module rules
│
└── infrastructure/
    ├── docker/                      docker-compose.yml, .env.example
    ├── postgres/
    │   ├── core/rva_core_init.sql           rva_core: topology and simulation schemas
    │   └── telemetry/telemetry_init.sql     rva_telemetry: telemetry hypertable
    └── mosquitto/mosquitto.conf     MQTT broker (development configuration)
```

## Inside a module

Every module implementation project is organized by **feature (vertical slice)**, not by technical layer. There are no `Controllers/`, `Services/`, or `Repositories/` folders.

```text
Rva.Modules.<Area>/
├── Features/
│   └── <FeatureName>/       endpoint, command/query, handler, validator, request/response DTOs
├── Domain/                  module-internal entities and value objects shared by this module's features
├── Persistence/             the module's own DbContext and EF Core Fluent API configurations
└── <Area>Module.cs          the only public type: Add<Area>Module() and Map<Area>Endpoints()

Rva.Modules.<Area>.Contracts/
├── I<Area>Queries.cs        in-process query interfaces for other modules
├── *Snapshot.cs             immutable records returned by those queries (never entities)
└── *Event.cs                integration events the module publishes
```
