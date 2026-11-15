# Agentic Implementation Workflow

## Scope and Human Checkpoints

- Treat plans as roadmaps, not authorization to implement every step. Before implementation, a human selects or approves the specific plan step and its acceptance criteria.
- Work on one approved plan step per branch and pull request. Do not continue to later steps unless separately authorized.
- If a step is ambiguous, spans module ownership boundaries, or depends on unfinished work, stop and ask the human to clarify or split/reorder the step. Do not silently expand scope.
- Agents may analyze, propose changes, implement, test, and prepare pull requests. A human reviews the complete diff and validation results, decides whether to merge, and approves deployments. Agent output or review is not a substitute for human approval.
- 'Evaluate', 'assess', 'check alignment' and 'review' mean analysis only. Report findings and proposed changes, then wait before implementing.

## Test-first steps

A step can be done test-first, with the tests and the implementation written by different agents. The point is that the tests come from the acceptance criteria and the implementation is not allowed to shape them. The agent definitions are in `.agents/agents/`.

1. **Tests first.** The test author (`.agents/agents/test-author.md`) writes the step's tests, plus stubs that throw `NotImplementedException` (.NET) or raise `NotImplementedError` (Python) so they compile. It runs them to confirm each new test fails for the right reason, and commits them on the step branch as `<plan-step-id>: tests first`.
2. **Human review.** A human reviews the tests-first commit against the acceptance criteria before implementation starts.
3. **Implementation.** The implementer (`.agents/agents/implementer.md`) starts only when given the step ID, the tests-first SHA, and confirmation that a human reviewed that commit. It makes the reviewed tests pass by filling in the stubs and adding whatever else the behavior needs. The implementer:
   * never modifies, deletes, skips, or weakens a test from the tests-first commit, and never changes its fixtures or fakes in a way that changes what a test checks;
   * never changes the signatures, contract types, or error codes the stubs define;
   * never adds public surface (see "Review items") without stopping first: it proposes the addition and continues only after a human approves it;
   * may add further tests, for example unit tests for internals it creates;
   * if a reviewed test or stub looks wrong, stops and reports it. The fix goes back through the test author and a human, in a new commit titled `<plan-step-id>: tests correction`; the implementer does not make it. A human reviews that commit as they reviewed the tests-first commit, and confirms it before the implementer continues.
4. **Pull request.** The implementer opens the pull request only when every test from the tests-first commit was run and passes, the step's other checks pass, and no stub still throws `NotImplementedException` or raises `NotImplementedError`. If any test fails or could not run, it stops with local commits and reports instead.

The pull request names the tests-first commit and carries the stub list from the test author's handoff. The reviewer checks both rules:

* **Tests unchanged:** `git diff <tests-first-sha>..HEAD --stat -- tests/ infrastructure/postgres/tests/` shows only new tests, or corrections approved through step 3.
* **Signatures unchanged:** the stubs live in `src/`, where the implementer legitimately changes the bodies, so a path diff cannot catch this. Compare each signature in the stub list against `git diff <tests-first-sha>..HEAD` for its file.

The `harness / tests-first` check (`.agents/skills/verify/SKILL.md`, "Harness checks") enforces the first rule on `step/*` pull requests: it fails when a line under `tests/` or `infrastructure/postgres/tests/` that existed at a reviewed commit (the tests-first commit or a `tests correction`) was modified or deleted afterwards, and when `NotImplementedException` or `NotImplementedError` is still under `src/`. It cannot see a test weakened by an added line (an early `return`, a new `Skip`), or a changed signature; the reviewer still checks both.

## Review items

A review item is anything an agent decided that a human has not: a **departure** from the plan, an **open choice** the plan and the tests left to the agent, or an **addition to the public surface**.

**Public surface** means a public type or member in a `.Contracts` project, an error code, or an HTTP route. All of it comes from the plan or the tests-first commit:

* The test author stubs every type, member, error code, and route the step's tests reach, including every error code a failure-path test asserts. Surface the plan does not name is called out in the handoff as a shape the human confirms at the tests-first review.
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
- [ ] For a test-first step, the description names the tests-first commit, and the implementation did not change the reviewed tests (see "Test-first steps").
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
- Open one pull request for that plan step, and include its ID, scope, acceptance criteria, tests run, and any risks or follow-up work in the description.
- Keep the change within the step's stated scope and module ownership. Put shared contracts, infrastructure, or cross-cutting work in an explicit plan step when practical; otherwise get human approval for the necessary scope before proceeding.
- After opening the pull request, stop. Do not merge it, start another plan step, or add unrelated changes. Address review feedback on the same branch and pull request.
- If the environment cannot create branches or pull requests, do not claim that it did. Make the scoped change only when authorized, and report the limitation and the exact branch/PR action a human must perform.

## Validation and Enforcement

- Run the relevant focused checks before requesting review and report their results accurately. Follow `.agents/skills/verify/SKILL.md` for repository commands.
- Never bypass failing required checks or branch protections. If a check cannot run or fails for an unrelated reason, explain that in the pull request for human assessment.
- **Current enforcement:** the repository is private on the GitHub Free plan, so the `main` ruleset is not enforced server-side. The shared hooks in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) block commits and pushes to `main` locally, and block commits that break the test framework standard (`.ai/rules/testing.md`); they can be bypassed with `--no-verify`, so never do that.
- These instructions guide agent behavior; they do not technically enforce branch or pull request policy. Protect `main` in the Git hosting configuration, require pull requests and status checks, and use CI to validate plan-step metadata or changed-path scope when that enforcement is needed.
