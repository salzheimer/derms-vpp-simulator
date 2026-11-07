# Agentic Implementation Workflow

## Scope and Human Checkpoints

- Treat plans as roadmaps, not authorization to implement every step. Before implementation, a human selects or approves the specific plan step and its acceptance criteria.
- Work on one approved plan step per branch and pull request. Do not continue to later steps unless separately authorized.
- If a step is ambiguous, spans module ownership boundaries, or depends on unfinished work, stop and ask the human to clarify or split/reorder the step. Do not silently expand scope.
- Agents may analyze, propose changes, implement, test, and prepare pull requests. A human reviews the complete diff and validation results, decides whether to merge, and approves deployments. Agent output or review is not a substitute for human approval.
- 'Evaluate', 'assess', 'check alignment' and 'review' mean analysis only. Report findings and proposed changes, then wait before implementing.

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
- [ ] Relevant local verification commands from `.ai/scripts/verify.md` were run, or skipped checks are explained.
- [ ] Architecture, module ownership, security, and data rules in `AGENTS.md` and `.ai/rules/` are respected.
- [ ] Tests cover the changed behavior, and existing tests were not weakened to make checks pass.
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

- Run the relevant focused checks before requesting review and report their results accurately. Follow `.ai/scripts/verify.md` for repository commands.
- Never bypass failing required checks or branch protections. If a check cannot run or fails for an unrelated reason, explain that in the pull request for human assessment.
- **Current enforcement:** the repository is private on the GitHub Free plan, so the `main` ruleset is not enforced server-side. The shared hooks in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) block commits and pushes to `main` locally, and block commits that break the test framework standard (`.ai/rules/testing.md`); they can be bypassed with `--no-verify`, so never do that.
- These instructions guide agent behavior; they do not technically enforce branch or pull request policy. Protect `main` in the Git hosting configuration, require pull requests and status checks, and use CI to validate plan-step metadata or changed-path scope when that enforcement is needed.
