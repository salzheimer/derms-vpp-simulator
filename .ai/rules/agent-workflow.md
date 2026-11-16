# Agentic Implementation Workflow

## Scope and Human Checkpoints

- Treat plans as roadmaps, not authorization to implement every step. Before implementation, a human selects or approves the specific plan step and its acceptance criteria.
- Work on one approved plan step per branch and pull request. Do not continue to later steps unless separately authorized.
- If a step is ambiguous, spans module ownership boundaries, or depends on unfinished work, stop and ask the human to clarify or split/reorder the step. Do not silently expand scope.
- Agents may analyze, propose changes, implement, test, and prepare pull requests. A human reviews the complete diff and validation results, decides whether to merge, and approves deployments. Agent output or review is not a substitute for human approval.
- 'Evaluate', 'assess', 'check alignment' and 'review' mean analysis only. Report findings and proposed changes, then wait before implementing.

## Test spec steps

A step can have a test spec, with the tests and the implementation written by different agents. The point is that the tests come from the acceptance criteria and the implementation is not allowed to shape them. The agent definitions are in `.agents/agents/`.

1. **Test spec.** The test author (`.agents/agents/test-author.md`) writes the step's tests, plus stubs that throw `NotImplementedException` (.NET) or raise `NotImplementedError` (Python) so they compile. It runs them to confirm each new test fails for the right reason, and commits them on the step branch as `<plan-step-id>: test spec`.
2. **Spec review.** A human reviews the spec commit against the acceptance criteria before implementation starts.
3. **Implementation.** The implementer (`.agents/agents/implementer.md`) starts only when given the step ID, the spec SHA, and confirmation that a human reviewed that commit. It makes the reviewed tests pass by filling in the stubs and adding whatever else the behavior needs. The implementer:
   * never modifies, deletes, skips, or weakens a test from the spec commit, and never changes its fixtures or fakes in a way that changes what a test checks;
   * never changes the signatures, contract types, or error codes the stubs define;
   * never adds public surface (see "Review items") without stopping first: it proposes the addition and continues only after a human approves it;
   * may add further tests, for example unit tests for internals it creates;
   * if a reviewed test or stub looks wrong, stops and reports it. The fix goes back through the test author and a human, in a new commit titled `<plan-step-id>: spec correction`; the implementer does not make it. A human reviews that commit as they reviewed the spec commit, and confirms it before the implementer continues.
4. **Pull request.** The implementer opens the pull request only when every test from the spec commit was run and passes, the step's other checks pass, and no stub still throws `NotImplementedException` or raises `NotImplementedError`. If any test fails or could not run, it stops with local commits and reports instead.

The `run-step` skill (`.agents/skills/run-step/SKILL.md`) runs these steps in order for one approved step: a preflight (dependencies done, no undecided review items they hold, clean tree, branch), the test author, a hard stop until a human confirms the spec review, then the implementer. It never merges.

### What the step's plan gives the agents

Plans differ in layout and numbering, so the agents look for each input by what it is, not by a section number. The plan for `p1-s01` (`.ai/plans/P1_S01_GRID_TOPOLOGY_PLAN.md`) is one example of where each lives.

| Input | What it is | Where `P1_S01_GRID_TOPOLOGY_PLAN.md` has it |
|---|---|---|
| Scope | What the step builds and what it leaves out | "Scope", and the step's row in "Parts" |
| Acceptance criteria | The checks that say the step is done | The step's row in "Parts" |
| Decisions | Fixed choices the tests and code must follow | "Decisions", by ID |
| Public surface | Types, members, error codes (`<Module>.<Code>`), and routes the step adds | "Project structure" and "Error-handling contract" |
| Error-handling contract | How expected failures are reported (kind, code, HTTP status), how unexpected failures behave, and what every failure test asserts | "Error-handling contract": the `Error.Kind` table, the rules U1–U5, and the fault injections FI-1..FI-8 |
| Test catalogue | The step's test IDs, what each checks, and whether it is a success, expected-failure, or unexpected-failure case | "Test catalogue", the step's subsection (Kind column: S, EF, UF) |
| Review items | Departures and open choices, with their decisions | Under "Parts" ("Departures in `p1-s01c`, for review", "Choices in `p1-s01d`, for review") |

A plan may keep an input in a document it links, or under its own heading: `P1_S00F_DDL_TESTS_PLAN.md` takes its test IDs (GT-01.., SIM-01..) from `docs/architecture/model-validation-scenarios.md` and its checks from "Verification". Follow the plan's links before deciding an input is missing. When a plan has no error-handling contract or test catalogue, the acceptance criteria are the only source: write tests from them, and list in the handoff what a contract or catalogue would have settled. When an input the step needs is missing or ambiguous, stop and report it; never fill the gap with a choice of your own.

The pull request names the spec commit and carries the stub list from the test author's handoff. The reviewer checks both rules:

* **Tests unchanged:** `git diff <spec-sha>..HEAD --stat -- tests/ infrastructure/postgres/tests/` shows only new tests, or corrections approved through step 3.
* **Signatures unchanged:** the stubs live in `src/`, where the implementer legitimately changes the bodies, so a path diff cannot catch this. Compare each signature in the stub list against `git diff <spec-sha>..HEAD` for its file.

The `harness / test-spec` check (`.agents/skills/verify/SKILL.md`, "Harness checks") enforces the first rule on `step/*` pull requests: it fails when a line under `tests/` or `infrastructure/postgres/tests/` that existed at a reviewed commit (the spec commit or a `spec correction`) was modified or deleted afterwards, and when `NotImplementedException` or `NotImplementedError` is still under `src/`. It cannot see a test weakened by an added line (an early `return`, a new `Skip`), or a changed signature, and it trusts the `spec correction` title without knowing whether a human reviewed that commit. The reviewer checks all three: the check's summary lists each correction commit to confirm.

## Review items

A review item is anything an agent decided that a human has not: a **departure** from the plan, an **open choice** the plan and the tests left to the agent, or an **addition to the public surface**.

**Public surface** means a public type or member in a `.Contracts` project, an error code, or an HTTP route. All of it comes from the plan or the spec commit:

* The test author stubs every type, member, error code, and route the step's tests reach, including every error code a failure-path test asserts. Surface the plan does not name is called out in the handoff as a shape the human confirms at the spec review.
* The implementer adds no public surface beyond the stubs without stopping first. It proposes the addition (what, where, and why the behavior needs it), and continues only after a human approves it. Internal types and members are not public surface.

**Where they are recorded.** Each item goes in a review-items table in the plan that defines the work, either under the part it belongs to or in one table with a Part column, with these columns: `#` (a short ID), the item, what was built or proposed, why, and **Decision**. The pull request that raises an item lists it in its "Review items" section (`.github/pull_request_template.md`) and links the table.

**Decisions are explicit.** A decision is `keep` or `change`, with who decided and when, for example `keep (S. A., 2026-11-14)`. An item with no recorded decision is `open`. Merging the pull request that raised an item does not decide it. A `change` decision names the follow-up that makes the change. An item recorded only for information is marked `information` and needs no decision.

**The decision gate.** A part may not start while an item is `open` in the review-items table of any part in its "Depends on" column, unless a human has marked that item `open (non-blocking, <who>, <date>)`. An agent asked to start such a part stops and lists the blocking items instead.

## Plan Status Tracking

- Record each work item's canonical status at the lowest level that has its own scope and acceptance criteria, in the plan that defines that work.
- Higher-level plans may show a summary, but derive it from or link to the lower-level status. Do not maintain an independent status for the same work item at multiple levels.
- When work is split into more detailed plan items, move canonical status tracking to those items and replace broader duplicated statuses with a summary or link.
- Update the canonical status in the pull request that changes the work item's state, including completion, deferral, or a blocker.

## Pre-review Checklist

Use this checklist for each pull request. The shared items stay the same; the plan-step acceptance criteria are specific to the change and belong in the plan and pull request description.

### Every pull request

- [ ] The pull request identifies the approved plan step and stays within its scope.
- [ ] The description summarizes the change, lists the step's acceptance criteria, records validation performed, and notes known risks or follow-up work.
- [ ] The diff is focused; unrelated changes are removed or explained.
- [ ] CI checks have completed successfully. Any failed or unavailable check is explained with its impact for human review.
- [ ] Relevant local verification commands from `.agents/skills/verify/SKILL.md` were run, or skipped checks are explained.
- [ ] Architecture, module ownership, security, and data rules in `AGENTS.md` and `.ai/rules/` are respected.
- [ ] Tests cover the changed behavior, and existing tests were not weakened to make checks pass.
- [ ] For a step with a test spec, the description names the spec commit, and the implementation did not change the reviewed tests (see "Test spec steps").
- [ ] Every review item (departure, open choice, public-surface addition) is in the plan's review-items table and in the description's "Review items" section; no public surface was added without approval (see "Review items").
- [ ] No review item this part depends on is still `open` and blocking (see "Review items", the decision gate).
- [ ] Affected documentation and plans are updated.
- [ ] A human reviewer has examined the diff and validation results.
- [ ] Do not merge while a CI check is failing or a material review concern is unresolved.

### Plan-step acceptance criteria

- [ ] The acceptance criteria for this specific step are linked or included in the pull request description.
- [ ] Each criterion is met, with validation evidence recorded in the pull request.
- [ ] Any unmet criterion is explicitly identified and agreed as follow-up before merge.

## Branch and Pull Request

- Start each implementation branch from the latest `main`. Use a short-lived branch named `step/<plan-step-id>-<slug>` (for example, `step/p1-s02-grid-topology`).
- To bring a step branch up to date, rebase it onto `main`; never merge `main` into it. After a spec commit, a merge that brings in test changes from `main` fails the `harness / test-spec` check. Do not use GitHub's "Update branch" button, which merges.
- Open one pull request for that plan step, and include its ID, scope, acceptance criteria, tests run, and any risks or follow-up work in the description.
- Keep the change within the step's stated scope and module ownership. Put shared contracts, infrastructure, or cross-cutting work in an explicit plan step when practical; otherwise get human approval for the necessary scope before proceeding.
- After opening the pull request, stop. Do not merge it, start another plan step, or add unrelated changes. Address review feedback on the same branch and pull request.
- If the environment cannot create branches or pull requests, do not claim that it did. Make the scoped change only when authorized, and report the limitation and the exact branch/PR action a human must perform.

## Validation and Enforcement

- Run the relevant focused checks before requesting review and report their results accurately. Follow `.agents/skills/verify/SKILL.md` for repository commands.
- Never bypass failing required checks or branch protections. If a check cannot run or fails for an unrelated reason, explain that in the pull request for human assessment.
- **Current enforcement:** the repository is private on the GitHub Free plan, so the `main` ruleset is not enforced server-side. The ruleset is written down in `.github/rulesets/main.json` (pull requests only, no force-push or deletion, and the `test-guardrail`, `references`, `contracts`, `test-spec`, and `hooks` checks required), and `.github/CODEOWNERS` names who reviews the harness, adapters, guard tests, and contracts. Both take effect when a human imports the ruleset on a plan that enforces it (`.ai/plans/AI_HARNESS_PLAN.md`, `ai-08`). Until then, nothing on GitHub stops a direct push or a merge with failing checks. The shared hooks in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) block commits and pushes to `main` locally, and block commits that break the test framework standard (`.ai/rules/testing.md`); they can be bypassed with `--no-verify`, so never do that. The agent hook `.agents/hooks/guard-git.py`, registered for Claude Code, Codex, and Cursor's agent, blocks `--no-verify`, overriding `core.hooksPath`, pushes to `main`, `gh pr merge`, and MCP tools that merge a pull request before they run; `.claude/settings.json` also denies the GitHub connector's merge tool outright. It is a convenience: an agent run without hooks gets through, so CI and server-side protection remain the gate.
- These instructions guide agent behavior; they do not technically enforce branch or pull request policy. Protect `main` in the Git hosting configuration, require pull requests and status checks, and use CI to validate plan-step metadata or changed-path scope when that enforcement is needed.
