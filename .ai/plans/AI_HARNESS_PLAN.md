# Plan ai: Harden the AI harness

**Status:** approved 2026-11-14; each part is approved before it starts; part statuses are tracked in the Parts table below · **Depends on:** nothing · **Precedes:** project work in `PHASE_1_PLAN.md` (sequencing in section 3)

## 1. Goal and scope

Harden the AI harness (instructions, agents, skills, adapters, and their enforcement) for AI-native development with a human in the loop, and move it toward a software factory, so that agent-driven work on RVA can continue without losing human control. The harness comes first. Project decisions and changes to the product code follow it (section 7) and are not part of this plan.

In scope: `AGENTS.md`, `.ai/` (rules, plans, reference), `.agents/` (agents, skills, the scripts skills run, evals), the tool adapters (`.claude/`, `.codex/`, `.cursor/`, `.github/copilot-instructions.md`, `.cursorrules`), `.githooks/`, and `.github/` (workflows, PR template, `CODEOWNERS`).

**Scope rule: no part changes a file under `src/` or `tests/`.** Checks may *read* those folders (CI jobs, scripts, agent replays). A part whose validation generates code (a scaffold, a replay, an eval) runs in a temporary worktree or throwaway branch that is discarded and never pushed or merged. Anything that needs a change under `src/` or `tests/` goes to section 7.

Out of scope: product features, test and contract changes in the codebase, resolving open plan decisions (X1–X13, Y1–Y6), and the sidecar's forecasting work.

This plan comes from three assessments made on 2026-11-13 and 2026-11-14: harness structure, content quality, and the split between rules and skills. The findings that drive it:

* The test-first rules are prose only. In `p1-s01d` the implementer added a public error code (`GridTopology.OccurredAtInvalid`, choice Y5) that the tests-first review never saw.
* Nothing checks that the reviewed tests are unchanged. A human runs `git diff` by eye.
* 19 review items (X1–X13, Y1–Y6) are still open, and nothing stops later parts from building on them.
* The Cursor wrappers repeat their rules, and two of them have drifted (`security.mdc`, `sql-ddl.mdc`). `.cursorrules` and the Copilot topic list are stale.
* The conventions GridTopology established (errors, `UnitOfWork`, as-of reads, HTTP mapping) exist only in `P1_S01_GRID_TOPOLOGY_PLAN.md`. The scaffolder skill predates them.
* Procedures (`verify.md`, `sql-ddl.md` §5–8, the test-first steps) are loaded as rules. Some of them belong in skills.
* `main` is not protected on the server. `.claude/settings.json` is `{}`. There are no agent evals.

## 2. Decisions

| # | Decision |
|---|---|
| H1 | **Two roots, one principle.** `.ai/` holds what agents must **know**: rules, reference, plans. `.agents/` holds what agents can **do**: agent definitions, skills, and the scripts skills run. Tool folders hold thin adapters only. `AGENTS.md` is the map and states this principle. |
| H2 | **Skills stay in `.agents/skills/`**, where some tools (Codex) discover them natively. Claude Code needs a wrapper in `.claude/skills/`. Check before relying on Cursor and Copilot skill support (section 6). |
| H3 | **A thin adapter is frontmatter plus one pointer.** A Cursor `.mdc` body is one line: `Follow @.ai/rules/<topic>.md, the canonical standard. This file only tells Cursor when to load it.` A skill or agent wrapper names its canonical file and holds only tool-specific notes, such as sandbox workarounds. |
| H4 | **Constraints stay rules; procedures become skills.** Any "never" or safety constraint must live in a rule, because a skill is loaded only when the agent chooses it. A skill links to its rules and does not repeat them. |
| H5 | **Enforcement order: CI and the server first, hooks second.** Claude Code hooks are a convenience for one tool. The real enforcement is CI checks that every tool's output passes, plus server-side branch protection. |
| H6 | **The harness lives outside the product code.** Harness checks are shell scripts and CI jobs under `.agents/` and `.github/`, and they add no packages. They read `src/` and `tests/` but never change them (scope rule, section 1). A check that would be stronger as a test inside `tests/` is recorded as a follow-up in section 7. |
| H7 | **Plan IDs** for this plan are `ai-NN`, with branches named `step/ai-NN-<slug>`, one PR per part, and the workflow in `agent-workflow.md`. |

## 3. Sequencing

The harness is hardened first, and project work resumes after it:

1. **Stage A, harness core (`ai-01`–`ai-18`, tiers 1–4).** Must be merged before project work in section 7 resumes, starting with the review-item decisions and then `p1-s01e`. `p1-s01e` adds the D19 event records to `.Contracts`, the largest new public surface so far, so the public-surface rules (`ai-04`) and the contract change check (`ai-05`) matter most there. `ai-08` depends on a human decision about the GitHub plan (section 5). If that decision is still open, Stage A can finish without `ai-08`, which is then completed as soon as it can be.
2. **Stage B, rules and feedback (`ai-19`–`ai-23`, tiers 5–6).** These may run alongside project work. `ai-19` and `ai-20` must be merged before any Phase 2 sidecar work starts.
3. **Project work (section 7)** follows Stage A in the order given there.

## 4. Parts

Each part is one branch and one PR. In every part, the acceptance criteria also include: `git diff --name-only origin/main...HEAD -- src/ tests/` is empty (scope rule); the `ci` and `test-guardrail` workflows pass, unchanged, as evidence the harness change broke nothing; and the adapter link check passes (from `ai-03` on).

### Tier 1: Consistency (Stage A)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-01` | **Thin adapters and relocating `verify.md`.** Reduce every `.cursor/rules/*.mdc` to frontmatter plus one pointer (H3). Add `src/Rva.Simulator/**` to the `csharp-monolith` globs. Delete `.cursorrules`. Make `.github/copilot-instructions.md` point to `AGENTS.md` and `.ai/rules/` without a topic list. Move `.ai/scripts/verify.md` to `.agents/skills/verify/SKILL.md` (skill frontmatter, a command table first and the explanations after it), add its Claude wrapper, and update every reference in one pass. Remove `.ai/scripts/`. | — | Done (PR #29) | Each `.mdc` body (after the frontmatter) is exactly one line containing `@.ai/rules/<same-name>.md`. `.cursorrules` is gone. `grep -rn "scripts/verify.md"` over the repo returns nothing. The verify skill opens with a table of commands. `.claude/skills/verify/SKILL.md` exists and points to the canonical file. |
| `ai-02` | **The `AGENTS.md` map.** State the know/do principle (H1). Add `docs/architecture/*` and its purpose. Add "how to find the next step" (`MASTER_PLAN` → phase plan → step plan Parts table → status). Add this plan to "Where to look next". Keep the GitHub Free and enforcement explanation only in `agent-workflow.md`, and link to it from `testing.md` and the verify skill. | `ai-01` | Done (PR #30) | `AGENTS.md` names what belongs in `.ai/`, `.agents/`, and the tool folders. The enforcement caveat appears in one file. Every path in `AGENTS.md` resolves. |
| `ai-03` | **Adapter and reference link check in CI.** A script (`.agents/skills/verify/scripts/check-references.sh` or similar) that checks every backticked or `@`-prefixed repo path in `AGENTS.md`, `.ai/`, `.agents/`, `.claude/`, `.codex/`, `.cursor/`, and `.github/*.md` exists, and that every `.mdc` body matches H3. It runs in a new CI job with no paths filter. | `ai-01` | Not started | The job fails on a planted broken path and on an `.mdc` with a second body line, and passes on `main`. It is documented in the verify skill. |

### Tier 2: Instruction hardening and the core checks (Stage A)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-04` | **Public surface and review items.** In `implementer.md`, any new public type or member in a `.Contracts` project, any new error code, or any new endpoint or route counts as a stop-and-report item, listed under "Additions for review" in the handoff and the PR. In `test-author.md`, all public surface comes from the plan, and a shape the agent had to choose is called out. In `agent-workflow.md`, write down the review-items practice (departures and open choices go in the plan's table with a keep/change decision, linked from the PR). Add the **decision gate**: a part cannot start while an earlier review item it depends on is still undecided. Add a "Review items" section to the PR template. | — | Not started | The three files and the template carry the rules. The rules agree with each other. The Cursor `agent-workflow.mdc` needs no change (it is a pointer after `ai-01`). |
| `ai-05` | **Contract change check in CI** (H6). A script under `.agents/skills/verify/scripts/`, run by a new PR job with `fetch-depth: 0`. It lists every file changed under `src/**/*.Contracts/`, every added or removed `public const string` error code there, and every added or removed route mapping (`Map(Get\|Post\|Put\|Patch\|Delete)`) under `src/`. It writes them to the job summary and fails unless the PR description's "Review items" section (from `ai-04`) names each changed Contracts file. A file listed there without a human decision on it is reported, not passed silently. | `ai-04` | Not started | Replayed in a temporary worktree against the `p1-s01d` merge: it reports `GridTopologyErrors.cs` and `OccurredAtInvalid`, and fails without a "Review items" entry. It passes with the entry, and on a PR with no Contracts change. The job and its failure message are described in the verify skill and `implementer.md`. |
| `ai-06` | **Tests-first CI check.** A script under `.agents/skills/verify/scripts/`, run by a job on `step/*` PRs (`fetch-depth: 0`). It finds the commit titled `<id>: tests first`, and **skips with a notice** if there is none, since not every step is test-first. It fails if any line under `tests/` or `infrastructure/postgres/tests/` that existed at that commit was modified or deleted (added lines and new files are allowed). It also fails if any file under `src/` contains `NotImplementedException` or `NotImplementedError`. Corrections approved through the workflow use a marker documented in `agent-workflow.md` (for example a `<id>: tests correction` commit). | — | Not started | On a throwaway replay branch built from `p1-s01d` (never pushed): passes as merged; fails when one reviewed assertion is edited; fails when a stub's `NotImplementedException` is left in place; skips on a branch without a tests-first commit. The job is described in `agent-workflow.md` and the verify skill. |

### Tier 3: Enforcement (Stage A)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-07` | **Claude Code hooks and sandbox settings** (convenience layer, H5). Add a `PreToolUse` hook that denies `git commit --no-verify`, pushes to `main`, and `gh pr merge`. Move the dotnet and Docker sandbox workarounds out of the prose in the agent wrappers and into sandbox settings where the settings support it; check the exact keys against the current Claude Code docs. **Human applies:** `.claude/settings.json` cannot be written from the agent sandbox. | — | Not started | Each denied command is blocked in a Claude Code session. The agent wrappers keep only the workarounds the settings cannot express. |
| `ai-08` | **Server-side protection and `CODEOWNERS`.** Agent: add `.github/CODEOWNERS` covering `AGENTS.md`, `.ai/`, `.agents/`, `.github/`, `tests/Rva.ArchitectureTests/`, `infrastructure/postgres/tests/`, and `src/**/*.Contracts/` (this changes only `.github/`). **Human:** decide the GitHub plan or repository visibility, then turn on a `main` ruleset requiring PRs, code-owner review, and the checks `ci`, `test-guardrail`, `ai-03`, `ai-05`, and `ai-06`. | `ai-03`, `ai-06` | Not started | A direct push to `main` is rejected by GitHub. A PR with a failing required check cannot be merged. `agent-workflow.md` "Current enforcement" is updated to say so. |

### Tier 4: Generic agents and skills (Stage A)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-09` | **Agent definitions that work for any plan.** Replace GridTopology-specific references in `test-author.md` and `implementer.md` ("sections 4 and 6", U5, `FXA-`, "for a GridTopology part") with roles ("the plan's error-handling contract and test catalogue", "the shared fixtures"), keeping GridTopology only as a labelled example. | `ai-04` | Not started | No instruction depends on one plan's section numbers. An agent given only the definition and another module's plan can find every input it needs. |
| `ai-10` | **`run-step` skill.** Chains one step from start to finish: preflight (the step is approved, its dependencies are done, no open review items block it under `ai-04`, the tree is clean, the branch exists), then the test author, then a **hard stop for human review** of the tests-first commit, then the implementer with the SHA. It never merges. | `ai-04`, `ai-06`, `ai-09` | Not started | The skill refuses to start when any preflight check fails. It cannot run the implementer without an explicit human confirmation. A dry run on a past step, in a temporary worktree that is then discarded, produces the right handoffs. |
| `ai-11` | **Reviewer agent** (`.agents/agents/reviewer.md`, plus Claude and Codex wrappers). Advisory only. It runs the pre-review checklist, reads the `ai-05` and `ai-06` results, checks each acceptance criterion against its evidence, lists "Additions for review" and open review items, and posts the findings for the human. It never approves or merges. | `ai-04`–`ai-06` | Not started | Run on the PRs for `p1-s01c` and `p1-s01d`, it reports the X and Y items and the Y5 addition. It changes no files. |
| `ai-12` | **Automatic check selection in the verify skill.** A script that maps changed paths (`git diff --name-only origin/main...`) to the checks to run (.NET build and test, `run_tests.sh`, image builds, the sidecar once tooling exists), runs them, and reports each as passed, failed, or not run, with the reason. | `ai-01` | Not started | For a change touching only `infrastructure/postgres/`, it selects `run_tests.sh` and the integration tests; for a docs-only change, it selects only the link check. Its output is what the handoffs and PRs record. |
| `ai-13` | **`record-decision` skill.** Settles one review item (an X or Y row): records keep or change with the date and the decider, amends the affected plan sections, and links the PR. A human makes the decision; the skill only records it. | `ai-04` | Not started | Applied to one X item in a dry run, it updates the plan's table and sections consistently. |
| `ai-14` | **`write-step-plan` skill.** Captures the structure that makes test-first work, based on `P1_S01_GRID_TOPOLOGY_PLAN.md`: scope, decisions, project structure, error-handling contract (EF/UF), parts with acceptance criteria, a test catalogue with IDs, traceability, risks, verification, and a review-items table. | `ai-09` | Not started | A plan drafted with it for `p1-s02` has every section. The test author can work from it without asking for missing inputs. |
| `ai-15` | **Module conventions rule.** A new `.ai/rules/module-conventions.md` (or a section in `csharp-monolith.md`) covering: `Result` and `ErrorKind` and the HTTP status for each; error-code naming (`<Module>.<Code>`, in `.Contracts`); the EF/UF contract and problem details that leak no internals; `UnitOfWork` and the change context; `TimeProvider`; as-of reads and current reads in one REPEATABLE READ transaction; options validated at startup; readiness checks; and the `Http/` and `Features/Reads/` folders. GridTopology is the reference implementation. Replace the matching text in `P1_S01_GRID_TOPOLOGY_PLAN.md` with links to the rule. Add a Cursor pointer. | `ai-01` | Not started | Every convention listed is in the rule, with a GridTopology file as its example. `csharp-monolith.md` §4 lists the real folders. Where open review items (X4, X6) affect a convention, the rule names them as pending. |
| `ai-16` | **Rewrite the scaffolder skill.** Matches what GridTopology really looks like: `Http/`, `<Area>Options`, `<Area>Errors` in `.Contracts`, `UnitOfWork`, readiness check, outbox; the module's schema and DDL (via the `ai-18` skill); both test projects (`.Tests` and `.Integration.Tests`, D3); and the exact `ProjectReference` in `Rva.ArchitectureTests.csproj` that `Architecture_tests_reference_every_backend_project` requires. | `ai-15`, `ai-18` | Not started | In a temporary worktree, a throwaway module scaffolded with the skill builds and passes the architecture tests with no manual fixes. The worktree is discarded; nothing under `src/` or `tests/` is committed. |
| `ai-17` | **`add-feature-slice` skill.** Adds one feature to a module: endpoint, handler, `Result` and error codes, HTTP mapping, reads, `UnitOfWork`, unit and integration tests. It applies `ai-15` and does not repeat it. | `ai-15` | Not started | In a temporary worktree, replaying one `p1-s01c` read feature with the skill produces the same structure as the merged code. The worktree is discarded. |
| `ai-18` | **`schema-change` skill, and splitting `sql-ddl.md`.** Move the procedures in `sql-ddl.md` §5–8 into the skill, in order: pgTAP test first, DDL, comments with facts tags, `enable_versioning`, the self-check list, `data-model.md`, `run_tests.sh`. Include a comment template. `sql-ddl.md` keeps §1–4 and the "never weaken the lint" constraint, and links to the skill. | `ai-01` | Not started | No constraint is lost from `sql-ddl.md`; each "never" or "must" in §5–8 either stays in the rule or is restated there. The skill's steps cover the §8 checklist. |

### Tier 5: Rule content (Stage B; before Phase 2)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-19` | **`security.md`: what applies now and what is planned.** Now: secrets and no passwords in config; no internal details in error responses (as `ProblemAssertions` checks); actor and change reason on every write; AI output treated as untrusted input, always capped by envelopes and node ratings. Planned, each marked with its phase: the Audit module and `IAuditProvider` (Phase 4), JWT service-to-service (Phase 2), OIDC RBAC (Phase 3). | `ai-01` | Not started | No statement in the rule refers to something that does not exist without saying when it arrives. |
| `ai-20` | **Expand `python-sidecar.md`.** The shape of a recommendation (an advisory flag, confidence, a valid-until time, the model version); input validation; no write paths to the grid; data sources that match `requirements.txt` (no TimescaleDB driver today); project layout; test and lint commands (pointing to the verify skill). | `ai-01` | Not started | Every statement in the rule matches `requirements.txt` and `main.py`. The Consultant constraint can be checked by a test once tooling exists (section 7). |

### Tier 6: Feedback (Stage B)

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `ai-21` | **Glossary terms.** Add: constraint path, effective grid state, grid state event, as-of read, EF/UF, fail-safe read. Remove the historical `BatteryState` note. | — | Not started | Every term used in a test name or plan ID in `P1_S01_GRID_TOPOLOGY_PLAN.md` that a newcomer would not know is defined. |
| `ai-22` | **Agent evals.** Replay merged steps (`p1-s01c`, `p1-s01d`) as fixtures. Eval definitions, planted defects (as patches), and recorded results live under `.agents/evals/`. Each run checks out its step in a temporary worktree, applies the patches there, runs the agent, scores it, and discards the worktree. The test author must cover every test ID in the step. The implementer must stop on a planted wrong test and must report a planted new public member. The reviewer must find the X and Y items. Record the results per agent-definition version. | `ai-09`, `ai-11` | Not started | A documented command runs the evals. A change to an agent definition can be compared against the last recorded results. No eval run leaves a change under `src/` or `tests/` in the working tree. |
| `ai-23` | **Factory metrics.** A script that uses the commit and branch conventions to report, per step: lead time, how long the step waited at the human gate (tests-first to implementation), how often agent runs stopped and why, and how long review items stayed open. | `ai-04` | Not started | A report for every `p1-s01*` step can be generated from git history alone. |

## 5. Human actions

These cannot be done by an agent from this environment, or they are a human's decision:

* **Approve this plan** and each part's acceptance criteria before work starts (`agent-workflow.md`).
* **`.claude/settings.json` and `.claude/skills/`** are write-denied in the agent sandbox. The `ai-07` hooks and every new Claude skill wrapper (`ai-01`, `ai-10`, `ai-12`–`ai-14`, `ai-16`–`ai-18`) need a human edit or explicit approval for an unsandboxed write.
* **Branch protection (`ai-08`):** choose a GitHub plan or repository visibility that enforces rulesets on `main`. Check GitHub's current plan matrix.
* **Contract changes (`ai-05`):** a human decides each "Review items" entry the check reports.

## 6. Checks to make before relying on them

* Whether your Cursor version supports skills in `.agents/skills/`. If it doesn't, the matching rule points to the skill file instead (H2).
* Whether Codex discovers `.agents/skills/` natively for every skill, including new ones, so no Codex skill wrappers are needed.
* The exact Claude Code sandbox and hook setting keys for `ai-07`.

## 7. After the harness: project decisions and changes

Not part of this plan. Listed in the order they should follow Stage A, because the harness depends on them or exposes them. Each one goes through its owning plan and the normal workflow, using the hardened harness:

1. **Settle X1–X13 (`p1-s01c`) and Y1–Y6 (`p1-s01d`).** Each is a human keep/change decision, recorded with `ai-13`. Under the `ai-04` gate, `p1-s01e` can start only once the items it depends on are decided (probably X1 and X7, since its writes extend the same contracts). `p1-s01g` needs X6 and X11.
2. **Contract surface snapshot test** (changes `tests/`). A test in `tests/Rva.ArchitectureTests` that dumps every `.Contracts` project's public types and members, and its error codes, by reflection, and compares them with committed approved files using xUnit `Assert`. It is stronger than the `ai-05` path check because it sees signatures, not just changed files. A human approves each update to an approved file. Do this as its own step before `p1-s01e`, if possible.
3. **Resume Phase 1** with `p1-s01e`, using `run-step` (`ai-10`) and the reviewer (`ai-11`).
4. **Sidecar test tooling** (pytest, ruff, a first test, a CI job; changes `src/Rva.Intelligence`) as its own step, before any Phase 2 sidecar work.
5. **Read-only MCP server** for Postgres and Timescale, once Phase 3 telemetry exists. Until then, no placeholder config (`.mcp/README.md`).

## 8. Risks

| Risk | Mitigation |
|---|---|
| **R1** The `ai-04` decision gate blocks `p1-s01e` on open X items. | Settle the items `p1-s01e` depends on first, which is a short human review. Don't weaken the gate. |
| **R2** The `ai-05` check works on changed files and lines, not compiled signatures, so it can miss a change it can't recognize (for example a public member added in a non-Contracts file the contracts expose). | It catches the Y5 case. The reflection snapshot test (section 7, item 2) closes the rest once project work resumes. |
| **R3** The `ai-06` check flags legitimate test corrections. | Corrections go through the test author and a human in a marked commit, which the check recognizes. |
| **R4** Moving procedures into skills means an agent that never loads the skill misses them. | H4: every constraint stays in a rule, and the agent definitions name the skills they must use. |
| **R5** Hooks give a false sense of enforcement. | H5: hooks cover one tool only. CI and server-side protection (`ai-08`) are the real gate. |
| **R6** Harness work delays project work. | This is intended (section 3). Stage A is mostly small, text-only parts, and Stage B runs alongside project work. |

## 9. Verification

Each part's PR records the checks named in its acceptance criteria plus the shared ones in section 4. After `ai-12`, those are the checks the verify skill selects. Until `ai-08`, a human confirms that every required check passed before merging.
