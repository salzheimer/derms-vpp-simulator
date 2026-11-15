#!/usr/bin/env bash
# Tests-first check (AI_HARNESS_PLAN.md, ai-06; the rule is .ai/rules/agent-workflow.md, "Test-first steps").
#
# In a test-first step, a human reviewed the tests in the "<plan-step-id>: tests first" commit before anyone
# implemented them. This check fails when:
#   1. a line under tests/ or infrastructure/postgres/tests/ that existed at the reviewed commit was modified or
#      deleted afterwards. Added lines and new files are allowed. A renamed or moved file counts as deleted.
#   2. a file under src/ still contains NotImplementedException or NotImplementedError at the head.
#
# Corrections: when a reviewed test is wrong, the test author fixes it in a commit titled
# "<plan-step-id>: tests correction", which a human reviews like the tests-first commit. Each such commit starts a
# new reviewed baseline: changes inside it are not checked, and changes between two reviewed commits are checked
# against the earlier one. Commits are walked along the first-parent chain; merge commits are skipped as markers.
#
# With no tests-first commit in the range, the step is not test-first: the check prints a notice and passes.
#
# Usage: check-tests-first.sh [--base <ref>] [--head <ref>]
#   --base defaults to origin/main and --head to HEAD; the range is merge-base(base, head)..head.
#   Writes a Markdown report to $GITHUB_STEP_SUMMARY when it is set (GitHub Actions), and to stdout.
# Exit status: 0 when it passes or skips, 1 when it fails, 2 on usage errors.
# Reads git history only; it never changes the working tree. Runs on bash 3.2+ (macOS) and Linux.

set -u

base=origin/main
head=HEAD
while [ $# -gt 0 ]; do
    case $1 in
        --base) base=$2; shift 2 ;;
        --head) head=$2; shift 2 ;;
        *) echo "check-tests-first: unknown argument $1" >&2; exit 2 ;;
    esac
done

from=$(git merge-base "$base" "$head") || { echo "check-tests-first: no merge base for $base and $head" >&2; exit 2; }
head_sha=$(git rev-parse "$head") || exit 2

# Reviewed commits, oldest first: the last tests-first commit, then each tests correction after it.
commits=$(git log --first-parent --no-merges --reverse --format='%H %s' "$from..$head")
tests_first=$(printf '%s\n' "$commits" | grep -E '^[0-9a-f]+ [^ ]+: tests first$' | tail -1 | cut -d' ' -f1)

emit() {
    printf '%s\n' "$1"
    [ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$1" >> "$GITHUB_STEP_SUMMARY"
}

if [ -z "$tests_first" ]; then
    emit "## Tests-first check

Skipped: no \`<plan-step-id>: tests first\` commit between \`$(git rev-parse --short "$from")\` and \`$(git rev-parse --short "$head_sha")\`, so this is not a test-first step."
    [ -n "${GITHUB_ACTIONS:-}" ] && echo "::notice title=Tests-first check skipped::No '<plan-step-id>: tests first' commit in this pull request."
    exit 0
fi

reviewed=$(printf '%s\n' "$commits" | sed -n "/^$tests_first /,\$p" |
           grep -E '^[0-9a-f]+ [^ ]+: tests (first|correction)$' | cut -d' ' -f1)

tests=':(glob)tests/**'
pgtests=':(glob)infrastructure/postgres/tests/**'

# Check 1, per segment: from each reviewed commit to the commit before the next one (or the head).
changed=
removed=
segments=
set -- $reviewed
while [ $# -gt 0 ]; do
    start=$1; shift
    if [ $# -gt 0 ]; then end=$(git rev-parse "$1^"); else end=$head_sha; fi
    segments="$segments
- \`$(git rev-parse --short "$start")\`..\`$(git rev-parse --short "$end")\`"
    [ "$start" = "$end" ] && continue
    # --no-renames: a moved test file shows as deleted, so its old lines count as removed.
    files=$(git diff --no-renames --numstat "$start" "$end" -- "$tests" "$pgtests" | awk '$2 != "0" { print $3 }')
    for f in $files; do
        changed="$changed $f"
        lines=$(git diff --no-renames -U0 "$start" "$end" -- "$f" |
                awk '/^@@/ { hunk = 1; next } /^diff --git/ { hunk = 0 } hunk && /^-/ { print }')
        removed="$removed
$f ($(git rev-parse --short "$start")..$(git rev-parse --short "$end")):
$lines"
    done
done

# Check 2: stubs left in production code at the head.
stubs=$(git grep -n -E 'NotImplemented(Exception|Error)' "$head_sha" -- src | sed "s/^$head_sha://")

report() {
    echo "## Tests-first check"
    echo
    echo "Tests-first commit \`$(git rev-parse --short "$tests_first")\`. Reviewed tests are compared over these ranges (each starts at a reviewed commit: the tests-first commit or a \`tests correction\`):"
    echo "$segments"
    echo
    if [ -n "$changed" ]; then
        echo "**Fail:** reviewed test lines were modified or deleted (\`-\` lines below). Only added lines and new files are allowed after review. If a reviewed test is wrong, stop and report it; the fix goes through the test author and a human, in a \`<plan-step-id>: tests correction\` commit."
        echo
        echo '```diff'
        printf '%s\n' "$removed" | sed '1{/^$/d;}' | head -200
        echo '```'
    else
        echo "Reviewed tests: unchanged (only added lines or new files)."
    fi
    echo
    if [ -n "$stubs" ]; then
        echo "**Fail:** stubs remain under \`src/\`. Replace each with the real behavior:"
        echo
        printf '%s\n' "$stubs" | sed -E 's/^(.*)$/- `\1`/'
    else
        echo "Stubs: none left under \`src/\`."
    fi
}

emit "$(report)"
[ -z "$changed" ] && [ -z "$stubs" ]
