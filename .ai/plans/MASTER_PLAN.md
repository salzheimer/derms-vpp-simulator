
# Project Master Plan: Resilient VPP Aggregator (RVA)

## 1. Executive Summary
RVA is a high-concurrency energy orchestration platform for 500+ Distributed Energy Resources (DERs). It bridges the gap between AI-driven economic forecasting and the physical realities of the power grid through "Cyber-Physical" guardrails.

**Core Innovation:** A "Zero-Trust" command pipeline that utilizes a Reconciliation Loop to bridge the gap between AI "Intent" and Grid "Reality."

**Identity:** RVA is a **utility-integrated, grid-aware VPP**, modeled on a Portland General Electric–style program: the utility orchestrates enrolled customer DERs to serve both the distribution grid (below the substation) and the bulk system (above it). RVA is not a full DERMS — it models the grid only as deeply as needed to respect constraints. A **third-party aggregator** operating model is also supported, through the variation points in section 2.

---

## 2. Operating Model
Both VPP models share the same assets, grid hierarchy, telemetry, device integrations, dispatch execution, Reconciliation Loop, and Chaos Engine. They differ in exactly three places, each isolated behind one abstraction so the aggregator model is additive rather than a rewrite:

| Variation point | Utility-integrated (built first) | Third-party aggregator | Abstraction |
|---|---|---|---|
| **Source of grid limits** | Computed by RVA from its own node ratings | Received from the utility's DERMS | `OperatingEnvelope` (node, interval, import/export kW, `source`). Dispatch always reads limits through one contract and never knows the source. |
| **Reason to dispatch** | Utility program events (peak events, distribution relief) | Market commitments (cleared bids, DR contracts) | `FlexibilityCommitment` — deliver X kW from a portfolio during a window. Programs and market awards are two ways to create one. |
| **Settlement** | Per-event customer incentive (e.g. $/kWh credit) | Market revenue share | A settlement rule on each program or commitment, computed from the same delivered-kWh measurement. |

* **Per program, not per deployment.** Each program records its operator (utility or aggregator), value stream, and settlement rule. The same fleet can serve both models at once.
* **Grid safety always wins.** Whatever the source of a dispatch, it is capped by the applicable operating envelope.
* **Simulation of the aggregator model:** `Rva.Simulator` stands in for external parties — a simulated utility DERMS issuing envelopes (including adverse ones) and market price/award inputs. RVA does not simulate market clearing.

`OperatingEnvelope`, `FlexibilityCommitment`, programs, and settlement are delivered in later phases; Phase 1 only needs node capacity ratings and the rule that safety-critical reads go through the owning module's contract.

---

## 3. Technical Stack
- **Backend (`src/Rva.Backend`):** .NET 10 Modular Monolith (C#). A single `Rva.Host` host composes isolated module projects (`Rva.Modules.*`) that share only a minimal `Rva.SharedKernel`. See `.ai/rules/csharp-monolith.md`.
- **AI Sidecar (`src/Rva.Intelligence`):** Python 3.12 (FastAPI + PyTorch) for LSTM-based forecasting. Separate process.
- **Simulator (`src/Rva.Simulator`):** Monte Carlo Chaos Engine with fault injection. Separate process that talks to the backend only over HTTP and MQTT.
- **Data:** PostgreSQL + PostGIS (`rva_core`) for topology and simulation ledgers; TimescaleDB (`rva_telemetry`) for time-series telemetry; Redis for real-time state caching.
- **Messaging:** Eclipse Mosquitto (MQTT) with CloudEvents envelopes.
- **Deployment:** one utility per deployment. A second utility runs as a separate deployment with its own host, databases, Redis prefix, and MQTT topic root.

---

## 4. Implementation Roadmap

### Phase 1: Ingestion, Geospatial & Chaos (Weeks 1-3)
See `.ai/plans/PHASE_1_PLAN.md` for full implementation detail and the Definition of Done checklist.

- [ ] **Foundation:** Solution skeleton, `Rva.Host` host, `Rva.SharedKernel`, architecture test suite, and Docker infrastructure (both databases, Mosquitto, Redis).
- [ ] **GridTopology module:** Hierarchical grid topology (`Substation` → `Feeder` → `NeighborhoodTransformer` → `Premise`), generic `Asset`, and the `technology_types` / `interconnection_types` lookups.
- [ ] **Simulation module:** Run ledger (`simulation_runs`, `simulation_run_status_types`) and per-run asset overrides (`simulation_asset_states`), isolating each run's state.
- [ ] **Telemetry module:** High-throughput MQTT ingestion with CloudEvents, an Anti-Corruption Layer using `protocol_mappings`, and the `asset_telemetry` hypertable.
- [ ] **Chaos Engine (`Rva.Simulator`):** 50,000+ node fleet simulation using zero-allocation integer-indexed state, driven by run configuration from the backend.

### Phase 2: AI & Resilience (Weeks 4-6)
- [ ] **AI Sidecar:** Implement Python LSTM for load forecasting.
- [ ] **Resilience:** Wrap AI calls in **Polly Circuit Breakers**.
- [ ] **Auth:** Implement JWT-based Service-to-Service (S2S) authentication.
- [ ] **Logic:** Implement "Safe-Fail" defaults if the AI sidecar is unhealthy or "tripped."

### Phase 3: Reconciliation & Observability (Weeks 7-8)
- [ ] **The Reconciliation Loop:** Background service to compare "Commanded State" vs "Actual Telemetry" (Calculating Variance).
- [ ] **Dashboard:** React + TypeScript UI visualizing fleet health, spatial distribution, and variance.
- [ ] **Identity:** Integrate OIDC for User RBAC (`Operator` vs `Auditor`).

### Phase 4: Compliance & Cloud Hardening (Weeks 9-10)
- [ ] **Audit:** Implement an Audit module with **Serilog.AuditTo** for immutable command logging.
- [ ] **IaC:** Terraform scripts for Azure/AWS (Key Vault, Managed DB, Container Apps).
- [ ] **Edge Logic:** Finalize "Deadman's Switch" for local device fallback during comms loss.

New modules introduced in later phases follow the same structure as the Phase 1 modules (`.ai/rules/csharp-monolith.md`).

---

For AI-agent guidance (engineering principles, conventions, domain rules), see `AGENTS.md` and `.ai/rules/` at the repo root.
