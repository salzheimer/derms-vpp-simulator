#!/usr/bin/env bash
# Contract change check (AI_HARNESS_PLAN.md, ai-05; the rule is .ai/rules/agent-workflow.md, "Review items").
#
# Reports the public-surface changes in a pull request and fails unless the description's "## Review items" section
# names every changed .Contracts file:
#   * files changed under src/**/*.Contracts/ (these must be named);
#   * error codes added or removed there: public const string values of the form "<Module>.<Code>" (reported);
#   * HTTP route mappings (Map{Get,Post,Put,Patch,Delete}) added or removed anywhere under src/ (reported).
#
# In a test-first step, the tests-first commit (subject "<plan-step-id>: tests first") was reviewed by a human, so
# changes are measured from that commit instead of the base: only what the implementer added must be named.
#
# A file counts as named when its repo path or its file name appears in backticks in the "## Review items" section
# (HTML comments are ignored). Naming a file records it for review; it does not decide it. The human's decision goes
# in the plan's review-items table.
#
# Usage: check-contract-changes.sh --body-file <file> [--base <ref>] [--head <ref>]
#   --base defaults to origin/main and --head to HEAD; the range is merge-base(base, head)..head.
#   Writes a Markdown report to $GITHUB_STEP_SUMMARY when it is set (GitHub Actions), and to stdout.
# Exit status: 0 when every changed Contracts file is named (or none changed), 1 when one is not, 2 on usage errors.
# Reads git history only; it never changes the working tree. Runs on bash 3.2+ (macOS) and Linux.

set -u

base=origin/main
head=HEAD
body_file=
while [ $# -gt 0 ]; do
    case $1 in
        --base) base=$2; shift 2 ;;
        --head) head=$2; shift 2 ;;
        --body-file) body_file=$2; shift 2 ;;
        *) echo "check-contract-changes: unknown argument $1" >&2; exit 2 ;;
    esac
done
[ -n "$body_file" ] && [ -f "$body_file" ] || { echo "check-contract-changes: --body-file <file> is required" >&2; exit 2; }

from=$(git merge-base "$base" "$head") || { echo "check-contract-changes: no merge base for $base and $head" >&2; exit 2; }
tests_first=$(git log --format='%H %s' "$from..$head" | grep -E '^[0-9a-f]+ [^ ]+: tests first$' | tail -1 | cut -d' ' -f1)
[ -n "$tests_first" ] && from=$tests_first

contracts=':(glob)src/**/*.Contracts/**'
changed=$(git diff --name-only "$from" "$head" -- "$contracts")
# Error codes are the constants whose value follows the "<Module>.<Code>" convention, e.g. "GridTopology.AssetNotFound".
codes=$(git diff -U0 "$from" "$head" -- "$contracts" |
        grep -E '^[+-][^+-].*public const string [A-Za-z0-9_]+ *= *"[A-Z][A-Za-z0-9]*\.[A-Za-z0-9]+"' |
        sed -E 's/^([+-]).*public const string [A-Za-z0-9_]+ *= *"([^"]+)".*/\1 \2/')
routes=$(git diff -U0 "$from" "$head" -- src | grep -E '^[+-][^+-].*\.Map(Get|Post|Put|Patch|Delete)\(' |
         sed -E 's/^([+-])[[:space:]]*/\1 /')

# The "## Review items" section, without HTML comments, and the backticked tokens in it.
section=$(perl -0pe 's/<!--.*?-->//gs' "$body_file" | awk '/^## /{on = ($0 ~ /^## Review items[[:space:]]*$/); next} on')
named=$(printf '%s\n' "$section" | grep -oE '`[^`]+`' | tr -d '`')

missing=
listed=
for f in $changed; do
    if printf '%s\n' "$named" | grep -qxF -e "$f" -e "$(basename "$f")"; then
        listed="$listed $f"
    else
        missing="$missing $f"
    fi
done

report() {
    echo "## Contract change check"
    echo
    if [ -n "$tests_first" ]; then
        echo "Measured from the tests-first commit \`$(git rev-parse --short "$tests_first")\` to \`$(git rev-parse --short "$head")\`: changes in that commit were reviewed with the tests."
    else
        echo "Measured from the merge base \`$(git rev-parse --short "$from")\` to \`$(git rev-parse --short "$head")\`."
    fi
    echo
    if [ -z "$changed" ] && [ -z "$routes" ]; then
        echo "No public-surface changes."
        return
    fi
    echo "| Contracts file | Named in \"Review items\" |"
    echo "|---|---|"
    for f in $changed; do
        case " $missing " in *" $f "*) echo "| \`$f\` | **no** |" ;; *) echo "| \`$f\` | yes |" ;; esac
    done
    [ -n "$changed" ] || echo "| (none) | |"
    echo
    if [ -n "$codes" ]; then
        echo "Error codes (\`+\` added, \`-\` removed):"
        echo
        printf '%s\n' "$codes" | sed -E 's/^([+-]) (.*)$/- \1 `\2`/'
        echo
    fi
    if [ -n "$routes" ]; then
        echo "Route mappings under \`src/\` (\`+\` added, \`-\` removed):"
        echo
        printf '%s\n' "$routes" | sed -E 's/^([+-]) (.*)$/- \1 `\2`/'
        echo
    fi
    if [ -n "$missing" ]; then
        echo "**Fail:** name each Contracts file marked **no** in the description's \"Review items\" section, and record the change in the plan's review-items table (\`.ai/rules/agent-workflow.md\`, \"Review items\")."
    else
        echo "Every changed Contracts file is named. Naming records an item for review; a human decides it in the plan's review-items table."
    fi
}

out=$(report)
printf '%s\n' "$out"
[ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$out" >> "$GITHUB_STEP_SUMMARY"
[ -z "$missing" ]
