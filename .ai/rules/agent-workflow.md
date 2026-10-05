# Agentic Implementation Workflow

## Scope and Human Checkpoints

- Treat plans as roadmaps, not authorization to implement every step. Before implementation, a human selects or approves the specific plan step and its acceptance criteria.
- Work on one approved plan step per branch and pull request. Do not continue to later steps unless separately authorized.
- If a step is ambiguous, spans module ownership boundaries, or depends on unfinished work, stop and ask the human to clarify or split/reorder the step. Do not silently expand scope.
- Agents may analyze, propose changes, implement, test, and prepare pull requests. A human reviews the complete diff and validation results, decides whether to merge, and approves deployments. Agent output or review is not a substitute for human approval.

## Branch and Pull Request

- Start each implementation branch from the latest `main`. Use a short-lived branch named `step/<plan-step-id>-<slug>` (for example, `step/p1-s02-grid-topology`).
- Open one pull request for that plan step, and include its ID, scope, acceptance criteria, tests run, and any risks or follow-up work in the description.
- Keep the change within the step's stated scope and module ownership. Put shared contracts, infrastructure, or cross-cutting work in an explicit plan step when practical; otherwise get human approval for the necessary scope before proceeding.
- After opening the pull request, stop. Do not merge it, start another plan step, or add unrelated changes. Address review feedback on the same branch and pull request.
- If the environment cannot create branches or pull requests, do not claim that it did. Make the scoped change only when authorized, and report the limitation and the exact branch/PR action a human must perform.

## Validation and Enforcement

- Run the relevant focused checks before requesting review and report their results accurately. Follow `.ai/scripts/verify.md` for repository commands.
- Never bypass failing required checks or branch protections. If a check cannot run or fails for an unrelated reason, explain that in the pull request for human assessment.
- **Current enforcement:** the repository is private on the GitHub Free plan, so the `main` ruleset is not enforced server-side. The shared hooks in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) block commits and pushes to `main` locally; they can be bypassed with `--no-verify`, so never do that.
- These instructions guide agent behavior; they do not technically enforce branch or pull request policy. Protect `main` in the Git hosting configuration, require pull requests and status checks, and use CI to validate plan-step metadata or changed-path scope when that enforcement is needed.