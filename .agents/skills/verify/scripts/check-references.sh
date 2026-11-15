#!/usr/bin/env bash
# Checks the AI harness's references (AI_HARNESS_PLAN.md, ai-03):
#   1. Every repo path named in the agent instructions exists: backticked paths, @-references, and relative
#      Markdown links in the instruction files (*.md, *.mdc, *.toml) of AGENTS.md, CLAUDE.md, .ai/rules/, .agents/,
#      .claude/, .codex/, .cursor/rules/, and .github/*.md. Scripts and settings files are not scanned. Plans (.ai/plans/) are not checked: they name files that past steps removed and future
#      steps will add.
#   2. Every Cursor rule (.cursor/rules/*.mdc) is a thin adapter: frontmatter, then exactly one body line,
#      "Follow @.ai/rules/<same name>.md, ...", pointing at a rule that exists.
#   3. Every hook adapter (.claude/settings.json, .codex/hooks.json, .cursor/hooks.json) is thin: each hook "command"
#      runs a script under .agents/hooks/ that exists.
#   4. Every hook adapter still registers the git guard for shell commands and MCP calls, and .claude/settings.json
#      still denies the GitHub MCP merge tool.
#
# A backticked token counts as a path only if it starts with a top-level repo entry (see ROOTS) and holds no
# placeholder or command syntax (spaces, <, >, *, {, }, $, |). Build output (/bin/, /obj/) is skipped, since a
# fresh checkout has none. Trailing #anchors and :line suffixes are ignored.
#
# Usage: check-references.sh [repo-root]   (default: the git top level of the current directory)
# Exit status: 0 when every check passes, 1 otherwise. Runs on bash 3.2+ (macOS) and Linux.

set -u

root=${1:-$(git rev-parse --show-toplevel)}
cd "$root" || { echo "check-references: cannot enter $root" >&2; exit 2; }

ROOTS='AGENTS.md CLAUDE.md README.md global.json Directory.Packages.props VppAggregator.slnx .ai/ .agents/ .claude/ .codex/ .cursor/ .github/ .githooks/ .mcp/ docs/ infrastructure/ src/ tests/'

failures=0
fail() { echo "  $1"; failures=$((failures + 1)); }

# Tracked files when the root is a git work tree (as in CI); otherwise every file under the scanned folders.
list_files() {
    if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        git ls-files -- AGENTS.md CLAUDE.md .ai/rules .agents .claude .codex .cursor/rules '.github/*.md'
    else
        for f in AGENTS.md CLAUDE.md; do [ -f "$f" ] && echo "$f"; done
        find .ai/rules .agents .claude .codex .cursor/rules -type f 2>/dev/null | sed 's|^\./||'
        find .github -maxdepth 1 -type f -name '*.md' 2>/dev/null | sed 's|^\./||'
    fi
}

is_repo_path() {
    case $1 in *' '*|*'<'*|*'>'*|*'*'*|*'{'*|*'}'*|*'$'*|*'|'*) return 1 ;; esac
    case $1 in */bin/*|*/obj/*) return 1 ;; esac   # build output: exists only after a build, never in a fresh checkout
    for r in $ROOTS; do
        case $1 in "$r"|"$r"*) return 0 ;; esac
    done
    return 1
}

clean() {  # strip ./, a #anchor, a :line suffix, and trailing sentence punctuation
    p=${1#./}
    p=${p%%#*}
    p=$(printf '%s' "$p" | sed -E 's/:[0-9]+(-[0-9]+)?$//; s/[.,;:)]+$//')
    printf '%s' "$p"
}

echo "check-references: repo paths in agent instructions"
for file in $(list_files); do
    # Instruction files only (Markdown, Cursor rules, Codex TOML). Scripts and settings are code, not instructions:
    # backticks there are shell or data syntax, not path references.
    case $file in *.md|*.mdc|*.toml) ;; *) continue ;; esac
    dir=$(dirname "$file")

    # Backticked tokens and @-references, resolved from the repo root.
    { grep -oE '`[^`]+`' "$file" | tr -d '`'; grep -oE '@\.?[A-Za-z0-9_][A-Za-z0-9_./-]*' "$file" | sed 's/^@//'; } |
    while IFS= read -r token; do
        p=$(clean "$token")
        is_repo_path "$p" || continue
        [ -e "$p" ] || echo "$file: $p"
    done

    # Relative Markdown links, resolved from the file's own folder.
    grep -oE '\]\([^)]+\)' "$file" | sed -E 's/^\]\(//; s/\)$//' |
    while IFS= read -r link; do
        case $link in http://*|https://*|mailto:*|\#*) continue ;; esac
        p=$(clean "$link")
        [ -n "$p" ] || continue
        [ -e "$dir/$p" ] || echo "$file: $link (link)"
    done
done > "${TMPDIR:-/tmp}/check-references.$$"
while IFS= read -r line; do fail "missing: $line"; done < "${TMPDIR:-/tmp}/check-references.$$"
rm -f "${TMPDIR:-/tmp}/check-references.$$"

echo "check-references: Cursor rules are thin adapters"
for mdc in .cursor/rules/*.mdc; do
    [ -e "$mdc" ] || continue
    name=$(basename "$mdc" .mdc)
    body=$(awk 'n >= 2 { print } /^---$/ { n++ }' "$mdc" | grep -v '^[[:space:]]*$')
    count=$(printf '%s\n' "$body" | grep -c .)
    if [ "$count" -ne 1 ]; then
        fail "$mdc: body has $count non-empty lines; expected one pointer line"
    elif ! printf '%s' "$body" | grep -q "^Follow @\.ai/rules/$name\.md, "; then
        fail "$mdc: body must start with \"Follow @.ai/rules/$name.md, \""
    elif [ ! -f ".ai/rules/$name.md" ]; then
        fail "$mdc: points at .ai/rules/$name.md, which does not exist"
    fi
done

echo "check-references: hook adapters run canonical hooks"
# Each "command" in a hook adapter must run a script under .agents/hooks/ that exists (AI_HARNESS_PLAN.md, ai-07).
for adapter in .claude/settings.json .codex/hooks.json .cursor/hooks.json; do
    [ -f "$adapter" ] || continue
    grep -oE '"command"[[:space:]]*:[[:space:]]*"([^"\\]|\\.)*"' "$adapter" > "${TMPDIR:-/tmp}/check-references.$$"
    while IFS= read -r command; do
        scripts=$(printf '%s' "$command" | grep -oE '\.agents/hooks/[A-Za-z0-9_./-]+')
        if [ -z "$scripts" ]; then
            fail "$adapter: a hook command does not run a script under .agents/hooks/: $command"
        fi
        for s in $scripts; do
            [ -f "$s" ] || fail "$adapter: runs $s, which does not exist"
        done
    done < "${TMPDIR:-/tmp}/check-references.$$"
    rm -f "${TMPDIR:-/tmp}/check-references.$$"
done

echo "check-references: every hook adapter registers the git guard"
# The guard (.agents/hooks/guard-git.py) must stay registered for shell commands and MCP calls in every adapter, and
# the Claude Code deny rule for the GitHub merge tool must stay, so an agent cannot quietly drop its own guard.
python3 - <<'EOF' > "${TMPDIR:-/tmp}/check-references.$$"
import json, os
GUARD = ".agents/hooks/guard-git.py"
MERGE_TOOL = "mcp__claude_ai_github__merge_pull_request"
required = {
    ".claude/settings.json": [("PreToolUse", "Bash", "claude"), ("PreToolUse", "mcp__.*", "claude")],
    ".codex/hooks.json": [("PreToolUse", "Bash", "codex"), ("PreToolUse", "mcp__.*", "codex")],
    ".cursor/hooks.json": [("beforeShellExecution", None, "cursor"), ("beforeMCPExecution", None, "cursor")],
}
for path, needs in required.items():
    if not os.path.isfile(path):
        print(f"{path}: missing; it must register {GUARD}")
        continue
    try:
        data = json.load(open(path))
    except ValueError as e:
        print(f"{path}: not valid JSON ({e})")
        continue
    hooks = data.get("hooks", {}) if isinstance(data, dict) else {}
    for event, matcher, adapter in needs:
        commands = []
        for entry in hooks.get(event, []) or []:
            if not isinstance(entry, dict):
                continue
            if matcher is None:
                commands.append(entry.get("command", ""))
            elif entry.get("matcher") == matcher:
                commands += [h.get("command", "") for h in entry.get("hooks", []) if isinstance(h, dict)]
        if not any(GUARD in c and f"--adapter {adapter}" in c for c in commands):
            where = f"{event} (matcher {matcher})" if matcher else event
            print(f"{path}: {where} does not run {GUARD} --adapter {adapter}")
    if path == ".claude/settings.json":
        deny = (data.get("permissions") or {}).get("deny") or []
        if MERGE_TOOL not in deny:
            print(f"{path}: permissions.deny must include {MERGE_TOOL}")
EOF
while IFS= read -r line; do fail "$line"; done < "${TMPDIR:-/tmp}/check-references.$$"
rm -f "${TMPDIR:-/tmp}/check-references.$$"

if [ "$failures" -gt 0 ]; then
    echo "check-references: $failures problem(s). Fix the reference, or the file it points at, in this pull request." >&2
    exit 1
fi
echo "check-references: PASS"
