# Resilient VPP Aggregator (RVA)
### *Staff-Level Distributed Energy Resource Management*

RVA is a utility-integrated, grid-aware Virtual Power Plant (VPP) platform. It orchestrates 500+ Distributed Energy Resources (DERs), mostly home and grid batteries, across a substation hierarchy, so a fleet of small assets can act as one dependable resource during the 2026 "AI-Electrification" grid surge.

It closes the gap between AI-driven forecasting and the physical limits of the grid through **cyber-physical guardrails**: the AI proposes, the core decides, every command is capped by grid limits, and every result is checked against real telemetry.

## ⚡ Operating Model
RVA follows a utility-run VPP program: the utility enrolls customer DERs and dispatches them to relieve the distribution grid (below the substation) and support the bulk system (above it). RVA is not a full DERMS. It models the grid only as deeply as it needs to respect its limits.

A **third-party aggregator** model runs on the same fleet, grid model, telemetry and dispatch engine. The two models differ in exactly three places, each behind its own abstraction:

| | Utility-integrated | Third-party aggregator |
|---|---|---|
| **Grid limits** (`OperatingEnvelope`) | Computed from RVA's own node ratings | Received from the utility's DERMS |
| **Reason to dispatch** (`FlexibilityCommitment`) | Utility program events | Market commitments and DR contracts |
| **Settlement** | Per-event customer incentive | Market revenue share |

Each program records which model it uses, so one fleet can serve both at once. Whatever triggers a dispatch, **grid safety always wins**: no command exceeds the applicable operating envelope.

## 🚀 Key Architectural Features
- **Monte Carlo Fault Simulation:** A "Chaos Engine" simulates fleets of 50,000+ nodes and injects real-world hardware failures (thermal, voltage, comms) to test system resilience. It also plays the external parties: a utility DERMS issuing envelopes (including adverse ones) and market price and award inputs.
- **Cyber-Physical Guardrails:** The AI sidecar only gives advice; it never has dispatch authority. A **circuit breaker** isolates the grid core from sidecar latency or "hallucinations", and safe-fail defaults take over when the sidecar trips.
- **Geospatial-Aware Dispatch:** Discharge is balanced across individual substations, not just fleet-wide totals. A plan that looks fine for the whole fleet can still overload a single substation.
- **State Reconciliation:** A dedicated background service continuously calculates the variance between commanded power and real-world telemetry.
- **Edge Fallback:** A "Deadman's Switch" returns devices to safe local behavior when communications are lost.

## 🏗 System Architecture
The system uses a **Hybrid Modular Monolith** approach:
- **C# Core:** The source of truth for grid state, security, and dispatch. A single host composes isolated domain modules (grid topology, telemetry, simulation, audit, and more). Each module owns its own data and shares only explicit contracts.
- **Python Sidecar:** LSTM load forecasting and dispatch recommendations on PyTorch. A consultant, not the decision maker.
- **Chaos Engine:** A separate simulator process that talks to the core only over HTTP and MQTT, the same as real devices and external parties would.

## 🛡 Security & Compliance (NERC CIP Aligned)
- **Zero Trust:** Every device and service requires cryptographic identity verification. Service-to-service calls use JWT; operators sign in through OIDC.
- **Immutable Audit Trail:** All grid-impacting commands are recorded through a synchronous `AuditTo` pipeline to a tamper-evident, append-only store.
- **RBAC:** Strict separation between "Monitoring" (Auditor) and "Control" (Operator) roles.
- **Deployment Isolation:** One utility per deployment, each with its own host, databases, cache namespace, and MQTT topic root.

## 🛠 Tech Stack
- **Languages:** C# (.NET 10), Python 3.12 (FastAPI + PyTorch), TypeScript (React dashboard).
- **Data:** PostgreSQL + PostGIS (topology), TimescaleDB (telemetry), Redis (real-time state).
- **Messaging:** MQTT (Eclipse Mosquitto) with CloudEvents envelopes.
- **Infrastructure:** Docker, Terraform (Azure/AWS).
- **Patterns:** CQRS, Saga (Reconciliation), Anti-Corruption Layer, Circuit Breaker, Sidecar.
- **Testing:** xUnit v3 (Microsoft.Testing.Platform), pgTAP, pytest, and architecture tests that enforce module boundaries.

## 📚 Documentation
- [`.ai/plans/MASTER_PLAN.md`](.ai/plans/MASTER_PLAN.md): roadmap and phases.
- [`AGENTS.md`](AGENTS.md) and [`.ai/rules/`](.ai/rules/): engineering conventions and domain rules.

---
*Designed around the Portland/PNW grid, focusing on local grid resilience and distributed intelligence. RVA is an independent project. It is not affiliated with or endorsed by any utility, and all grid topology and asset data is synthetic.*
