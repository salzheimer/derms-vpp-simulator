# Resilient VPP Aggregator (RVA)
### *Staff-Level Distributed Energy Resource Management*

RVA is a high-performance orchestration engine designed to manage the complexity of the 2026 "AI-Electrification" grid surge. It provides a secure, scalable, and resilient platform for aggregating distributed battery storage.

## 🚀 Key Architectural Features
- **Monte Carlo Fault Simulation:** A built-in "Chaos Engine" that simulates real-world hardware failures (Thermal, Voltage, Comms) to test system resilience.
- **Cyber-Physical Guardrails:** Uses the **Circuit Breaker** pattern to isolate the grid core from AI sidecar latency or "hallucinations."
- **Geospatial-Aware Dispatch:** Prioritizes grid stability by balancing discharge requests across local substations.
- **State Reconciliation:** A dedicated background service that continuously calculates the variance between commanded power and real-world telemetry.

## 🏗 System Architecture
The system utilizes a **Hybrid Modular Monolith** approach:
- **C# Core:** Handles mission-critical state, security, and hardware orchestration.
- **Python Sidecar:** Handles high-intensity ML forecasting via PyTorch.
- **OpenTelemetry Pipeline:** Standardized observability for system logs and performance metrics.

## 🛡 Security & Compliance (NERC CIP Aligned)
- **Zero Trust:** Every device and service requires cryptographic identity verification.
- **Immutable Audit Trail:** All grid-impacting commands are recorded via a synchronous `AuditTo` pipeline to a tamper-evident database.
- **RBAC:** Strict separation between "Monitoring" (Auditor) and "Control" (Operator) roles.

## 🛠 Tech Stack
- **Languages:** C# (.NET 10), Python 3.12, TypeScript.
- **Infrastructure:** Docker, Terraform, MQTT (Mosquitto), TimescaleDB.
- **Patterns:** CQRS, Saga (Reconciliation), Anti-Corruption Layer, Sidecar.

---
*Developed for the Portland/PNW Smart Grid corridor, focusing on local grid resilience and distributed intelligence.*
