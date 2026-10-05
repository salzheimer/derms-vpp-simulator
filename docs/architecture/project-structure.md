# Project Structure

Where everything lives. The backend is a modular monolith built from separate `.csproj` projects, so module boundaries are enforced at compile time. The simulator and the AI sidecar are separate processes, not backend modules.

```text
vpp-aggregator/
├── AGENTS.md                        canonical AI-agent instructions (a map that links to .ai/)
├── CLAUDE.md, .cursorrules          thin pointers to AGENTS.md for specific tools
├── README.md                        human-facing overview
├── VppAggregator.slnx               .NET solution
├── .ai/
│   ├── rules/                       csharp-monolith, domain-glossary, security, python-sidecar, agent-workflow
│   ├── plans/                       MASTER_PLAN, PHASE_1_PLAN
│   └── scripts/verify.md            build, test, and lint commands
├── .agents/skills/                  canonical agent skills (e.g. csharp-module-scaffolder)
├── .claude/skills/                  stubs that point to .agents/skills
├── .github/copilot-instructions.md  pointer to AGENTS.md
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
