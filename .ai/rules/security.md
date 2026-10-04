# Security & Compliance Rules

Aligned to NERC CIP.

## Audit

- All grid-impacting commands are recorded via a synchronous `AuditTo` pipeline (Serilog) to a tamper-evident store.
- Audit logs are **append-only**. Never write code that edits or deletes existing audit records — only ever append.
- Audit writes go through an `IAuditProvider` contract exposed by the Audit module (`Rva.Modules.Audit.Contracts`, planned for Phase 4). Route audit writes through that contract rather than logging ad hoc; the Serilog `AuditTo` sink is configured in `Rva.Host`.

## Access control

- **Zero Trust** — every device and service requires cryptographic identity verification; do not assume network position implies trust.
- **RBAC** — strict separation between `Operator` (control) and `Auditor` (monitoring) roles. Do not implement an endpoint or command path that lets an Auditor trigger a grid-impacting action.
- Service-to-service auth uses JWT.

## Dispatch

Dispatch logic must prioritize substation-level balance (see `SubstationId` and the grid hierarchy in `.ai/rules/domain-glossary.md`), not just aggregate fleet totals — a fix that looks correct fleet-wide can still overload a single substation.
