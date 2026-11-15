# AGENTS.md

Canonical AI-agent instructions for the Resilient VPP Aggregator (RVA). Any AI coding tool working in this repo should start here. This file is a map, not an encyclopedia — see `.ai/rules/` for detail on each topic.

## What this project is

RVA is an orchestration platform for a Virtual Power Plant: managing 500+ Distributed Energy Resources (DERs, primarily home/grid batteries) across substations in the Portland/PNW grid. See `.ai/plans/MASTER_PLAN.md` for the executive summary and roadmap, and `README.md` for the human-facing pitch.

## Architecture: Hybrid Modular Monolith

- **C# core (`src/Rva.Backend`, .NET 10)** — the source of truth for grid state, security, and dispatch commands. Authoritative and mission-critical. A single `Rva.Host` host composes isolated module projects (`Rva.Modules.*`), each owning its own domain and data, vertically sliced by feature, and sharing only a minimal `Rva.SharedKernel`.
- **Python AI sidecar (`src/Rva.Intelligence`, FastAPI + PyTorch)** — produces load forecasts and dispatch *recommendations only*. It is a **Consultant, not the Decision Maker** — it never has direct dispatch authority.
- **Simulator (`src/Rva.Simulator`)** — Monte Carlo "Chaos Engine" for fault injection (thermal, voltage, comms) to test resilience. A separate process that talks to the backend only over HTTP and MQTT; it is not a backend module.

Full detail: `.ai/rules/csharp-monolith.md` and `.ai/rules/python-sidecar.md`.

## Key patterns

- **Circuit Breaker (Polly)** isolates the grid core from AI sidecar latency or bad output.
- **Reconciliation Loop** continuously compares commanded state vs. actual telemetry.
- **Anti-Corruption Layer (ACL)** on MQTT ingestion into the domain model.
- **CQRS / Saga** for command handling and the reconciliation workflow.
- **Immutable audit trail** via Serilog `AuditTo` — append-only, tamper-evident.

## How the AI layer is organized

- **`.ai/` is what agents must know:** rules (`.ai/rules/`, constraints that always hold for an area) and plans (`.ai/plans/`, the roadmap, steps, and their status). Every "never" and "must" lives in a rule.
- **`.agents/` is what agents can do:** agent definitions (`.agents/agents/`), skills (`.agents/skills/`, procedures loaded for a task, with any scripts they run), and agent hooks (`.agents/hooks/`, scripts every agent tool runs before a shell command). A skill links to the rules it applies and does not repeat them.
- **Tool folders hold thin adapters only:** `.claude/`, `.codex/`, `.cursor/rules/`, and `.github/copilot-instructions.md` point to the canonical files above and carry only tool-specific notes. The hook adapters (`.claude/settings.json`, `.codex/hooks.json`, `.cursor/hooks.json`) only register scripts from `.agents/hooks/`. Never put project guidance in an adapter; add it to the canonical file.

## Finding the next piece of work

`.ai/plans/MASTER_PLAN.md` (phases) → the phase plan (`PHASE_1_PLAN.md`, steps) → the step plan, such as `P1_S01_GRID_TOPOLOGY_PLAN.md`, whose Parts table holds each part's canonical status, dependencies, and acceptance criteria. Work on the AI harness itself is in `.ai/plans/AI_HARNESS_PLAN.md`. A part starts only when a human approves it (`.ai/rules/agent-workflow.md`).

## Where to look next

- `.ai/rules/agent-workflow.md` — human checkpoints, plan-step scope, branch/PR workflow for agents, test spec steps, and what is enforced where.
- `.agents/agents/` — canonical agent definitions (`test-author.md`, `implementer.md`); `.claude/agents/` and `.codex/agents/` hold thin wrappers that point at them.
- `.agents/skills/` — canonical skills: `verify` (build, test, and lint commands) and `csharp-module-scaffolder`; `.claude/skills/` holds thin wrappers.
- `.ai/rules/csharp-monolith.md` — C# module layout, conventions, sovereignty principle.
- `.ai/rules/sql-ddl.md` — SQL DDL standard: required table/column comments and their checked format, naming, types; read before writing or changing any SQL.
- `.ai/rules/python-sidecar.md` — sidecar conventions, the Consultant constraint.
- `.ai/rules/testing.md` — test frameworks (xUnit v3 on Microsoft.Testing.Platform, pgTAP, pytest), approved test packages, where test projects live; read before adding or changing any test project.
- `.ai/rules/domain-glossary.md` — domain terms (DER, `Asset`, `GridNode` hierarchy, `TechnologyType`, etc.).
- `.ai/rules/security.md` — audit, RBAC, NERC CIP, Zero Trust, geospatial dispatch rule.
- `.ai/plans/MASTER_PLAN.md` — roadmap and phases; `.ai/plans/AI_HARNESS_PLAN.md` — hardening the AI harness.
- `docs/architecture/` — diagrams of the target design (project structure, module dependencies, runtime, module contracts, data model, model validation scenarios). Rules win when they disagree; a change that affects a diagram updates it in the same pull request.

## Ground rules

- Do not give the Python sidecar dispatch authority — it advises, the C# core decides.
- Audit logs are append-only; never edit or delete existing audit records.
- Dispatch logic must respect substation-level balance, not just fleet-wide totals.
- Modules never touch another module's entities, `DbContext`, or tables — only its `.Contracts`.
