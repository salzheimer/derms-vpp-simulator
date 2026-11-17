#!/usr/bin/env bash
# Tests for select-checks.py (AI_HARNESS_PLAN.md, ai-12). Each case builds a throwaway git repo, commits a change on a
# branch, and checks which checks a --dry-run selects; a few cases run checks for real with fake commands on PATH.
#
# Usage: test-select-checks.sh   Exit status: 0 when every case passes, 1 otherwise. Needs bash, git, python3.

set -u
here=$(cd "$(dirname "$0")" && pwd)
S="$here/select-checks.py"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/select-checks-test.XXXXXX")
trap 'rm -rf "$tmp"' EXIT
export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid
pass=0
fail=0

repo() {  # repo <name>: a repo with origin/main and a few files, on branch work
    d="$tmp/$1"
    git init -q -b main "$d"
    mkdir -p "$d/src/Rva.Backend/Rva.Host" "$d/infrastructure/postgres/core" "$d/docs/architecture" "$d/infrastructure/docker"
    echo x > "$d/src/Rva.Backend/Rva.Host/Program.cs"
    echo x > "$d/infrastructure/postgres/core/init.sql"
    echo x > "$d/docs/architecture/project-structure.md"
    echo x > "$d/README.md"
    git -C "$d" add -A && git -C "$d" commit -qm init
    git -C "$d" update-ref refs/remotes/origin/main HEAD
    git -C "$d" switch -qc work
    echo "$d"
}

change() {  # change <dir> <message> <path>...: write each path and commit
    d=$1 m=$2; shift 2
    for p in "$@"; do mkdir -p "$d/$(dirname "$p")"; echo "$RANDOM" >> "$d/$p"; done
    git -C "$d" add -A && git -C "$d" commit -qm "$m"
}

row() {  # row <output> <check name>: the result cell of that check's row, or "absent"
    printf '%s\n' "$1" | awk -F' \\| ' -v n="| $2" '$1 == n { print $4; f=1 } END { if (!f) print "absent" }'
}

expect() {  # expect <case> <dir> <args> -- <check name>=<result prefix>...
    name=$1 dir=$2 args=$3; shift 4
    out=$(cd "$dir" && PATH="${XPATH:-}${XPATH:+:}$PATH" python3 "$S" $args 2>/dev/null)
    ok=1
    for pair in "$@"; do
        check=${pair%%=*} want=${pair#*=}
        got=$(row "$out" "$check")
        case "$got" in "$want"*) ;; *) ok=0; echo "FAIL $name: $check is '$got', want '$want'";; esac
    done
    if [ $ok -eq 1 ]; then pass=$((pass + 1)); else fail=$((fail + 1)); printf '%s\n' "$out" | sed 's/^/    /'; fi
}

status() {  # status <case> <want exit> <dir> <args> [PATH prefix]
    name=$1 want=$2 dir=$3 args=$4 pre=${5:-}
    (cd "$dir" && PATH="$pre${pre:+:}$PATH" python3 "$S" $args >/dev/null 2>&1)
    got=$?
    if [ "$got" -eq "$want" ]; then pass=$((pass + 1)); else fail=$((fail + 1)); echo "FAIL $name: exit $got, want $want"; fi
}

D=--dry-run

r=$(repo postgres); change "$r" schema infrastructure/postgres/core/init.sql
expect "postgres only" "$r" $D -- "Schema lint and pgTAP=**selected" "GridTopology integration tests=**selected" \
    ".NET build=absent" ".NET tests, all=absent" "Backend image=absent" "Test framework guardrail=absent" \
    "Harness references=**selected"

r=$(repo docs); change "$r" docs docs/architecture/project-structure.md README.md
expect "docs only" "$r" $D -- "Harness references=**selected" "Schema lint and pgTAP=absent" ".NET build=absent" \
    "Agent hooks=absent" "Contract changes=absent"
out=$(cd "$r" && python3 "$S" $D)
[ "$(printf '%s\n' "$out" | grep -c '^| ')" -eq 2 ] && pass=$((pass + 1)) || { fail=$((fail + 1)); echo "FAIL docs only: more than one check row"; }

r=$(repo scenarios); change "$r" scenarios docs/architecture/model-validation-scenarios.md
expect "scenarios doc mirrors db-tests" "$r" $D -- "Schema lint and pgTAP=**selected" ".NET build=absent"

r=$(repo src); change "$r" src src/Rva.Backend/Rva.Host/Program.cs
expect "backend source" "$r" $D -- ".NET build=**selected" ".NET tests, all=**selected" "Backend image=**selected" \
    "Contract changes=**not run**: needs the pull request description" "Simulator image=absent"

r=$(repo both); change "$r" both src/Rva.Backend/Rva.Host/Program.cs infrastructure/postgres/core/init.sql
expect "integration covered by the full run" "$r" $D -- "GridTopology integration tests=**covered" ".NET tests, all=**selected"

r=$(repo rename); git -C "$r" mv src/Rva.Backend/Rva.Host/Program.cs docs/Program.cs; git -C "$r" commit -qm move
expect "move out of src counts the old path" "$r" $D -- ".NET build=**selected" "Backend image=**selected"

r=$(repo msbuild); change "$r" msbuild src/Rva.Backend/Rva.Host/Rva.Host.csproj
expect "MSBuild file" "$r" $D -- "Test framework guardrail=**selected" ".NET build=**selected"

r=$(repo contracts); change "$r" contracts src/Rva.Backend/Modules/X/Rva.Modules.X.Contracts/IX.cs
echo "## Review items" > "$tmp/body.md"
expect "contracts with a description" "$r" "$D --body-file $tmp/body.md" -- "Contract changes=**selected"

r=$(repo sidecar); change "$r" sidecar src/Rva.Intelligence/main.py
expect "sidecar" "$r" $D -- "Sidecar image=**selected" "Sidecar tests and lint=**not available" ".NET build=absent"

r=$(repo hooks); change "$r" hooks .agents/hooks/guard-git.py
expect "hooks" "$r" $D -- "Agent hooks=**selected" "run-step scripts=absent"

r=$(repo spec); change "$r" "x: test spec" tests/A.cs
expect "spec commit selects test-spec" "$r" $D -- "Test spec=**selected" ".NET build=**selected"

r=$(repo uncovered); change "$r" compose infrastructure/docker/docker-compose.yml
out=$(cd "$r" && python3 "$S" $D)
printf '%s\n' "$out" | grep -q '^\* `infrastructure/docker/docker-compose.yml`' && pass=$((pass + 1)) \
    || { fail=$((fail + 1)); echo "FAIL uncovered path not listed"; printf '%s\n' "$out" | sed 's/^/    /'; }

r=$(repo worktree); echo y >> "$r/README.md"; mkdir -p "$r/infrastructure/postgres/x"; echo y > "$r/infrastructure/postgres/x/new.sql"
expect "committed range ignores the worktree" "$r" $D -- "Schema lint and pgTAP=absent"
expect "--worktree counts uncommitted and untracked" "$r" "$D --worktree" -- "Schema lint and pgTAP=**selected"

# Real runs with fake tools: a docs change runs only check-references.sh, faked here as a script in the repo.
fake="$tmp/bin"; mkdir -p "$fake"
printf '#!/bin/sh\nexit 1\n' > "$fake/docker"; chmod +x "$fake/docker"
r=$(repo run); mkdir -p "$r/.agents/skills/verify/scripts"
printf '#!/bin/sh\nexit 0\n' > "$r/.agents/skills/verify/scripts/check-references.sh"; chmod +x "$r/.agents/skills/verify/scripts/check-references.sh"
git -C "$r" add -A && git -C "$r" commit -qm "reference script"
change "$r" docs README.md
status "every selected check passed: exit 0" 0 "$r" ""
printf '#!/bin/sh\necho broken\nexit 1\n' > "$r/.agents/skills/verify/scripts/check-references.sh"
status "a failed check: exit 1" 1 "$r" "--worktree"
git -C "$r" checkout -q -- .agents
change "$r" schema infrastructure/postgres/core/init.sql
status "Docker unreachable: exit 3" 3 "$r" "" "$fake"
XPATH=$fake expect "Docker unreachable is reported" "$r" "" -- "Schema lint and pgTAP=**not run**: Docker is not reachable"
status "dry run: exit 0" 0 "$r" "$D" "$fake"
status "unknown argument: exit 2" 2 "$r" "--bogus"

echo "test-select-checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
