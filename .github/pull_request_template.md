## Summary

<!-- What changed, and why? -->

## Plan step and scope

- **Plan step:** <!-- e.g. p1-s01c; for work outside an implementation step, write N/A and explain why -->
- **Plan:** <!-- link to the plan file and section, or explain why this change has no plan step -->
- **Scope:** <!-- What is included? Confirm this PR stays within the approved scope. -->

## Acceptance criteria

<!-- List or link each acceptance criterion for this step. For each, describe the evidence that shows it is met. -->

- [ ] Criterion: <!-- criterion -->
  - Evidence: <!-- tests, behavior, or other verification -->

## Test-first

<!-- For a test-first step (.ai/rules/agent-workflow.md, "Test-first steps"). Otherwise write N/A. -->

- **Tests-first commit:** <!-- SHA of the `<plan-step-id>: tests first` commit -->
- **Stub list:** <!-- each type and signature the test author added, with its file -->
- **Self-check:** <!-- reviewed tests unchanged since that commit; stub signatures unchanged; no NotImplementedException / NotImplementedError left -->

## Review items

<!-- Anything an agent decided that a human has not (.ai/rules/agent-workflow.md, "Review items"). Each item is also in the
     plan's review-items table, with its Decision (keep / change / open). Merging this pull request does not decide them.
     Write "None" under a heading that has no items. Keep one item per bullet, with the file path in backticks: a check
     reads this section. In a test-first step, Contracts files whose stubs are in the "Stub list" above need no entry here. -->

### Public surface

<!-- Each public type or member in a .Contracts project, error code, or HTTP route added beyond the stubs. -->

- `<file>`: <!-- what was added, why, who approved it and when -->

### Departures and open choices

<!-- Each departure from the plan and each choice the plan left open, linked to its row in the plan's review-items table. -->

- <!-- ID: item, Decision (keep / change / open), link to the plan row -->

## Validation

<!-- List the verification commands/checks run and summarize results. Explain any skipped, failed, or unavailable checks. -->

- [ ] <!-- command or check and result -->

## Risks and follow-up

<!-- Note known risks, limitations, or follow-up work. Write "None" if there are none. -->

## Review checklist

- [ ] The diff is focused and matches the approved scope.
- [ ] Relevant architecture, module ownership, security, and data rules were followed.
- [ ] Tests cover the changed behavior; existing tests were not weakened to make checks pass.
- [ ] Every review item is listed above and in the plan's review-items table; no public surface was added without approval.
- [ ] Affected documentation and plan status were updated.
- [ ] CI checks completed successfully, or any failure/unavailable check is explained above.
- [ ] No material review concern or unmet acceptance criterion is being left unresolved.

See [the full pre-review checklist](../.ai/rules/agent-workflow.md#pre-review-checklist).
