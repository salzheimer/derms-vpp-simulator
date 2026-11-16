#!/usr/bin/env bash
# Tests for .agents/hooks/guard-git.py (AI_HARNESS_PLAN.md, ai-07).
#
# Feeds every case below to the hook in each tool's input shape (samples/claude.json, codex.json, cursor.json:
# recorded from each tool's documented hook input, with __COMMAND__ and __CWD__ filled in) and checks the exit
# status: 2 for "block", 0 for "allow". Plain pushes run in two throwaway repos, one on main and one on a step branch.
# Also checks the Claude adapter's Cursor skip, and that unreadable input is allowed.
#
# Usage: test-guard-git.sh   Exit status: 0 when every case passes, 1 otherwise. Needs bash, git, python3.

set -u
here=$(cd "$(dirname "$0")" && pwd)
hook="$here/../guard-git.py"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/guard-git.XXXXXX")
trap 'rm -rf "$tmp"' EXIT

for b in main step/ai-07-example; do
    d="$tmp/repo-${b##*/}"
    git init -q -b "$b" "$d"
    git -C "$d" -c user.name=t -c user.email=t@example.invalid commit -q --allow-empty -m init
done
on_main="$tmp/repo-main"
on_step="$tmp/repo-ai-07-example"

payload() {  # payload <tool> <cwd> <command>
    python3 - "$here/samples/$1.json" "$2" "$3" <<'EOF'
import json, sys
path, cwd, command = sys.argv[1:]
text = open(path).read()
text = text.replace('"__COMMAND__"', json.dumps(command)).replace('"__CWD__"', json.dumps(cwd))
print(text)
EOF
}

pass=0
fail=0
run() {  # run <expected: block|allow> <cwd> <command> [hook args...]
    expected=$1 cwd=$2 command=$3
    shift 3
    for tool in claude codex cursor; do
        payload "$tool" "$cwd" "$command" | "$hook" "$@" 2>/dev/null
        status=$?
        case $expected:$status in
            block:2|allow:0) pass=$((pass + 1)) ;;
            *) fail=$((fail + 1)); echo "FAIL [$tool] expected $expected, got exit $status: $command ${*:-}" ;;
        esac
    done
}

# Skipping hooks.
run block "$on_step" 'git commit --no-verify -m "wip"'
run block "$on_step" 'git commit -nm "wip"'
run block "$on_step" 'git commit -a -n -m wip'
run block "$on_step" 'git push --no-verify origin step/ai-07-example'
run block "$on_step" 'git -c core.hooksPath=/dev/null commit -m wip'
run block "$on_step" 'git config core.hooksPath /tmp/none'
run block "$on_step" 'git config --unset core.hooksPath'
run allow "$on_step" 'git config core.hooksPath .githooks'
run allow "$on_step" 'git config --get core.hooksPath'
run allow "$on_step" 'git commit -m "fix the -n flag and --no-verify docs"'
run allow "$on_step" 'git commit -am "wip"'
run block "$on_step" 'git commit -m "" -n'
run block "$on_step" 'git commit -m wip -n'   # -n after the message is still the flag
# Pushing to main.
run block "$on_step" 'git push origin main'
run block "$on_step" 'git push origin HEAD:main'
run block "$on_step" 'git push origin +step/x:refs/heads/main'
run block "$on_step" 'git push origin --delete main'
run block "$on_step" 'git push --all origin'
run block "$on_step" 'git -C . push origin main'
run block "$on_main" 'git push'
run block "$on_main" 'git push origin'
run allow "$on_step" 'git push'
run allow "$on_step" 'git push -u origin step/ai-07-example'
run allow "$on_step" 'git push origin main2'
# Merging pull requests.
run block "$on_step" 'gh pr merge 36 --merge'
run block "$on_step" 'gh api -X PUT repos/o/r/pulls/36/merge'
run allow "$on_step" 'gh pr create --base main --body-file pr.md'
run allow "$on_step" 'gh pr view 36'
# Compound and wrapped commands.
run block "$on_step" 'git add -A && git commit --no-verify -m x'
run block "$on_step" 'echo hi; git push origin main'
run block "$on_step" 'cd sub || true; gh pr merge 1'
run block "$on_step" 'bash -c "git push origin main"'
run block "$on_step" 'env GIT_TRACE=1 git push origin main'
run block "$on_step" 'HUSKY=0 git commit --no-verify -m x'
run block "$on_step" $'git status\ngit push origin main'
run allow "$on_step" 'git status && git log --oneline -3'
run allow "$on_step" 'echo "git push origin main"'
run allow "$on_step" 'dotnet build VppAggregator.slnx'
# Heredoc bodies and quoted multi-line arguments are data, not commands.
run allow "$on_step" $'git commit -F - <<\'EOF\'\ngh pr merge is blocked\ngit push origin main is blocked\nEOF'
run allow "$on_step" $'cat > pr.md <<EOF\ngit commit --no-verify\nEOF\ngit status'
run block "$on_step" $'cat > pr.md <<EOF\nnotes\nEOF\ngit push origin main'
run allow "$on_step" $'git commit -m "first line\ngh pr merge 1"'
run block "$on_step" $'git commit -m "x\ny" && gh pr merge 1'

# The Claude adapter blocks on every tool's input, including Cursor's (Cursor runs imported Claude Code hooks too).
for tool in claude codex cursor; do
    payload "$tool" "$on_step" 'git push origin main' | "$hook" --adapter claude 2>/dev/null
    [ $? -eq 2 ] && pass=$((pass + 1)) || { fail=$((fail + 1)); echo "FAIL [$tool] --adapter claude should block"; }
done

# MCP tool calls that merge a pull request are blocked, in each tool's MCP input shape; other MCP tools are allowed.
mcp() {  # mcp <expected exit> <json>
    printf '%s' "$2" | "$hook" 2>/dev/null
    [ $? -eq "$1" ] && pass=$((pass + 1)) || { fail=$((fail + 1)); echo "FAIL expected exit $1 for MCP input: $2"; }
}
mcp 2 '{"hook_event_name":"PreToolUse","tool_name":"mcp__claude_ai_github__merge_pull_request","tool_input":{"owner":"o","repo":"r","pullNumber":1}}'
mcp 2 '{"hook_event_name":"PreToolUse","tool_name":"mcp__github__merge_pull_request","tool_input":{"pull_number":1}}'
mcp 2 '{"hook_event_name":"beforeMCPExecution","cursor_version":"2.0.0","tool_name":"merge_pull_request","tool_input":"{}","command":"npx github-mcp"}'
mcp 0 '{"hook_event_name":"PreToolUse","tool_name":"mcp__claude_ai_github__create_pull_request","tool_input":{"title":"x"}}'
mcp 0 '{"hook_event_name":"PreToolUse","tool_name":"mcp__claude_ai_github__pull_request_read","tool_input":{}}'
mcp 0 '{"hook_event_name":"beforeMCPExecution","cursor_version":"2.0.0","tool_name":"list_issues","tool_input":"{}","command":"git push origin main"}'

# Unreadable or non-shell input is allowed.
for input in 'not json' '[]' '{"tool_name":"Read","tool_input":{"file_path":"x"}}'; do
    printf '%s' "$input" | "$hook" 2>/dev/null
    [ $? -eq 0 ] && pass=$((pass + 1)) || { fail=$((fail + 1)); echo "FAIL expected allow for input: $input"; }
done

# The reason reaches stderr.
msg=$(payload claude "$on_step" 'gh pr merge 1' | "$hook" 2>&1 >/dev/null)
case $msg in "Blocked by .agents/hooks/guard-git.py: gh pr merge"*) pass=$((pass + 1)) ;;
    *) fail=$((fail + 1)); echo "FAIL block message: $msg" ;; esac

msg=$(payload cursor "$on_step" 'gh pr merge 1' | "$hook" --adapter cursor 2>&1 >/dev/null)
case $msg in "Blocked by .agents/hooks/guard-git.py (--adapter cursor): "*) pass=$((pass + 1)) ;;
    *) fail=$((fail + 1)); echo "FAIL block message should name the adapter: $msg" ;; esac

echo "test-guard-git: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
