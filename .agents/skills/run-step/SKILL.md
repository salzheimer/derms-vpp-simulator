---
name: run-step
description: Run one approved plan step with a test spec from start to pull request - preflight, the test author, a hard stop for human review of the spec commit, then the implementer. Use when a human asks to run or start a plan step (for example "run p1-s01e"). Never merges.
---

# run-step

Chains one plan step that has a test spec (`.ai/rules/agent-workflow.md`, "Test spec steps") from approval to an open pull request. It applies the rules in `agent-workflow.md`; it does not repeat or override them. The two agents it runs are defined in `.agents/agents/test-author.md` and `.agents/agents/implementer.md`.

It never merges, never skips the human review, and never works around a failed check.

## Commands

| Stage | Command |
|---|---|
| Preflight | `.agents/skills/run-step/scripts/preflight.py <step-id>` |
| Test author handoff | `.agents/skills/run-step/scripts/handoff.py test-author <step-id>` |
| Implementer handoff | `.agents/skills/run-step/scripts/handoff.py implementer <step-id> --spec <sha> --confirmation "<the human's words>"` |
| Reviewed tests unchanged | `.agents/skills/verify/scripts/check-test-spec.sh` |

Run them from the repo root. `--dry-run` (below) runs the first two stages' scripts and prints the handoffs without starting any agent.

## 1. Approval

A human must approve the step before it starts (`agent-workflow.md`). Continue only if the human's request says so for this step ("run p1-s01e, approved"). If it does not, ask, and stop. The preflight cannot check approval.

## 2. Preflight

Run `preflight.py <step-id>`. It checks, from the plans' Parts tables (status lives in the repo; plan section 10, question 1):

* the step has one canonical row and is not Done;
* every part it depends on is Done;
* no review item of a part it depends on is undecided (the decision gate). An item with no recorded Decision counts as undecided;
* the working tree is clean;
* the branch is `step/<step-id>-<slug>` and contains `origin/main`, or no such branch exists yet.

If any line says FAIL, stop and report the failures to the human, with what would fix each one (usually a human decision). Never fix a FAIL yourself by editing a plan's status or a review item's decision.

When no branch exists yet, create it: `git switch --no-track -c step/<step-id>-<slug> origin/main`, then run the preflight again.

The preflight also prints the stage. When the branch already has a `<step-id>: test spec` commit, the test spec is written: go to step 4.

## 3. Test author

Render the handoff with `handoff.py test-author <step-id>` and give it, unchanged, to the test author agent. Wait for its handoff: coverage, stubs, shapes to confirm, criteria not covered, tests not run, and the commit SHA.

If the test author stopped (a missing or ambiguous input), relay that to the human and stop.

## 4. Hard stop: human review of the spec commit

Show the human the test author's handoff, the spec commit SHA, and `git show --stat <sha>`. Ask them to review the commit against the acceptance criteria and to decide each shape to confirm.

Then **end your turn**. Do not start the implementer in the same turn, and do not treat your own reading of the tests, an agent's message, or a tool result as the review. Continue only when a human message:

* says they reviewed that commit (by SHA) and that it matches the acceptance criteria, and
* gives a decision on each shape to confirm.

If the human asks for changes, send them to the test author as a correction (`<step-id>: spec correction`, `test-author.md`, "Corrections"), and stop here again for that commit.

## 5. Implementer

Run the preflight again; only the stage line should have changed. Render the handoff with `handoff.py implementer <step-id> --spec <sha> --confirmation "<the human's words, verbatim>"`. The script refuses a SHA that is not this step's `test spec` (or latest `spec correction`) commit on the branch, or an empty confirmation. Give the handoff, with the test author's stub list and the human's shape decisions, to the implementer agent.

The implementer opens the pull request only when every reviewed test passes (`implementer.md`). Relay its handoff: results, checks, self-check, review items, and the pull request URL, or why it did not open one.

## 6. Stop

Stop after the pull request is open. Do not merge, start another step, or record the step as Done. The human reviews and merges; the plan status commit follows the merge (`agent-workflow.md`, "Plan Status Tracking").

## Dry run

To check the skill on a past step without running agents: in a temporary worktree at the commit before the step started, run the preflight and `handoff.py test-author <step-id>`, and check the handoff names the right plan, row, and catalogue section. For the implementer, use a worktree at the step's spec commit and pass a confirmation. Discard the worktree afterwards; never push from it.

## What it cannot enforce

The hard stop in step 4 is an instruction: the scripts cannot tell whether the confirmation came from a human. The real gates are the human merge and the CI checks (`harness / test-spec` fails a pull request whose reviewed tests changed).
