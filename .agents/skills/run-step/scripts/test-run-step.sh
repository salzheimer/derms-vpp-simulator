#!/usr/bin/env bash
# Tests for the run-step scripts (AI_HARNESS_PLAN.md, ai-10): preflight.py on fixture plans, and handoff.py's refusals.
# Each case builds a throwaway git repo with a small .ai/plans/ and checks the exit status and one line of output.
#
# Usage: test-run-step.sh   Exit status: 0 when every case passes, 1 otherwise. Needs bash, git, python3.

set -u
here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d "${TMPDIR:-/tmp}/run-step.XXXXXX")
trap 'rm -rf "$tmp"' EXIT
# Throwaway repos need a commit identity; CI runners have none configured.
export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid
pass=0
fail=0

repo() {  # repo <name>: a git repo on main with origin/main, plans from stdin
    d="$tmp/$1"
    git init -q -b main "$d"
    mkdir -p "$d/.ai/plans"
    cat > "$d/.ai/plans/PLAN.md"
    git -C "$d" add -A
    git -C "$d" -c user.name=t -c user.email=t@example.invalid commit -q -m init
    git -C "$d" update-ref refs/remotes/origin/main HEAD
    echo "$d"
}

expect() {  # expect <name> <exit> <grep pattern> <dir> <command...>
    name=$1 want=$2 pattern=$3 dir=$4
    shift 4
    out=$(cd "$dir" && "$@" 2>&1)
    got=$?
    if [ "$got" -eq "$want" ] && printf '%s\n' "$out" | grep -qE -- "$pattern"; then
        pass=$((pass + 1))
    else
        fail=$((fail + 1)); echo "FAIL $name: exit $got (want $want), pattern '$pattern'"; printf '%s\n' "$out" | sed 's/^/    /'
    fi
}

plans='# Plan

## Parts

| Part | Scope | Depends on | Status | Acceptance criteria |
|---|---|---|---|---|
| `p9-s01a` | Base | — | Done (PR #1) | x |
| `p9-s01b` | Next | s01a | Not started | x |
| `p9-s01c` | After | s01a, s01b | Not started | x |
| `ai-01` | One | — | Done (PR #2) | x |
| `ai-02` | Two | — | Done (PR #3) | x |
| `ai-03` | Three | `ai-01`–`ai-02` | Not started | x |

### Choices in `p9-s01a`, for review

| # | Open point | Built | Why |
|---|---|---|---|
| Y1 | a | b | c |

## Review items

| ID | Part | Plan said | Built | Why | Decision |
|---|---|---|---|---|---|
| Z1 | `ai-01` | a | b | c | `keep (S. A., 2026-11-16)` |
| Z2 | `ai-02` | a | b | c | `open (non-blocking, S. A., 2026-11-16)` |
'
r=$(printf '%s' "$plans" | repo ok)
P="$here/preflight.py"
expect "range deps, decided items" 0 "PASS  dependency ai-02" "$r" "$P" ai-03
expect "short deps resolve to the phase" 1 "FAIL  dependency p9-s01b: \"Not started\"" "$r" "$P" p9-s01c
expect "item without Decision blocks" 1 "FAIL  review item Y1" "$r" "$P" p9-s01b
expect "done step refuses" 1 "FAIL  step row: .*Done" "$r" "$P" p9-s01a
expect "unknown step" 1 "found 0" "$r" "$P" p9-s99
echo x > "$r/dirty.txt"
expect "dirty tree" 1 "FAIL  working tree" "$r" "$P" ai-03
rm "$r/dirty.txt"
git -C "$r" switch -q -c step/ai-03-x
expect "on its step branch" 0 "PASS  branch: step/ai-03-x" "$r" "$P" ai-03
git -C "$r" switch -q main
expect "another step's branch exists" 1 "step/ai-03-x exists" "$r" "$P" ai-03
expect "open item blocks" 1 "FAIL  review item Z9" "$r" sh -c "sed -i.bak 's/^| Z2 .*/| Z9 | \`ai-02\` | a | b | c | \`open\` |/' .ai/plans/PLAN.md && rm .ai/plans/PLAN.md.bak && git commit -qam z9 && '$P' ai-03; s=\$?; git reset -q --hard HEAD~1; exit \$s"

# handoff.py refusals and success.
H="$here/handoff.py"
git -C "$r" switch -q step/ai-03-x
git -C "$r" -c user.name=t -c user.email=t@example.invalid commit -q --allow-empty -m "ai-03: test spec"
git -C "$r" -c user.name=t -c user.email=t@example.invalid commit -q --allow-empty -m "ai-03: implement"
expect "test author handoff" 0 "Commit the tests and stubs on this branch as \`ai-03: test spec\`" "$r" "$H" test-author ai-03
expect "no confirmation" 1 "refused. --confirmation" "$r" "$H" implementer ai-03 --spec HEAD~1
expect "wrong commit" 1 "refused. HEAD is \"ai-03: implement\"" "$r" "$H" implementer ai-03 --spec HEAD --confirmation "Reviewed it against the criteria"
expect "implementer handoff" 0 "Human confirmation, verbatim" "$r" "$H" implementer ai-03 --spec HEAD~1 --confirmation "Reviewed it against the criteria"
git -C "$r" -c user.name=t -c user.email=t@example.invalid commit -q --allow-empty -m "ai-03: spec correction"
expect "later correction needs its own review" 1 "later correction" "$r" "$H" implementer ai-03 --spec HEAD~2 --confirmation "Reviewed it against the criteria"

echo "test-run-step: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
