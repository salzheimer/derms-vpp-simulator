#!/usr/bin/env python3
"""Agent hook: block git and gh commands that skip the repo's safeguards (AI_HARNESS_PLAN.md, ai-07).

One script for every agent tool. Thin adapters register it as a pre-shell-command hook:
  .claude/settings.json  PreToolUse, matcher Bash   (Claude Code)
  .codex/hooks.json      PreToolUse, matcher Bash   (Codex)
  .cursor/hooks.json     beforeShellExecution       (Cursor's agent)

It reads the hook input (JSON) on stdin and finds the shell command in either shape:
  tool_input.command   Claude Code and Codex
  command              Cursor (beforeShellExecution)
It blocks by exiting 2 with the reason on stderr, which all three tools treat as "deny".

Blocked, in any segment of a compound command (&&, ||, ;, |, newlines, and `bash -c "..."`):
  * --no-verify on any git command, and `git commit -n`;
  * changing or unsetting core.hooksPath (`git -c core.hooksPath=...`, `git config core.hooksPath <other>`);
    setting it to .githooks, the documented setup, is allowed;
  * pushing to main: a refspec whose destination is main, --all or --mirror, or a plain `git push` while on main;
  * merging a pull request: `gh pr merge`, or `gh api` on a pulls/<n>/merge endpoint.

This is a convenience layer (H5), not enforcement: an agent run without hooks, or a command built in a way this
parser does not follow, gets through. CI and server-side protection are the gate (.ai/rules/agent-workflow.md,
"Validation and Enforcement"). Input it cannot read is allowed, with a note on stderr.

Usage: guard-git.py [--adapter claude|codex|cursor] < hook-input.json
  --adapter claude: skip when the input comes from Cursor (it has "cursor_version"). Cursor also loads Claude Code
  hooks from .claude/settings.json; its native .cursor/hooks.json entry already covers the command, so this keeps
  the check from running twice.
Exit status: 0 allow, 2 block.
"""

import json
import os
import re
import shlex
import subprocess
import sys

MAIN = "main"
ALLOWED_HOOKS_PATH = ".githooks"
WRAPPERS = {"env", "command", "sudo", "time", "nohup", "nice", "exec"}
SHELLS = {"sh", "bash", "zsh", "dash"}
OPERATORS = {"&&", "||", ";", "|", "&", ";;", "|&"}
# git global options that take a separate value.
GIT_VALUE_OPTS = {"-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path", "--config-env"}
# git commit options that take a separate value, and the short ones whose value can be attached (-mmsg).
COMMIT_VALUE_OPTS = {"-m", "--message", "-F", "--file", "-C", "--reuse-message", "-c", "--reedit-message",
                     "-t", "--template", "--author", "--date", "--cleanup", "--fixup", "--squash",
                     "--trailer", "--pathspec-from-file", "-S", "--gpg-sign"}
COMMIT_SHORT_WITH_VALUE = set("mFCctS")
PUSH_VALUE_OPTS = {"-o", "--push-option", "--repo", "--receive-pack", "--exec"}


def tokens(command):
    try:
        lexer = shlex.shlex(command, posix=True, punctuation_chars=";&|")
        lexer.whitespace_split = True
        return list(lexer)
    except ValueError:  # unbalanced quotes: fall back to plain words
        return command.split()


def segments(command):
    for line in command.splitlines() or [command]:
        current = []
        for tok in tokens(line):
            if tok in OPERATORS:
                if current:
                    yield current
                current = []
            else:
                current.append(tok)
        if current:
            yield current


def unwrap(seg):
    """Drop leading VAR=value assignments and wrappers (env, sudo, ...); return the command words."""
    i = 0
    while i < len(seg):
        word = seg[i]
        if re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", word):
            i += 1
        elif os.path.basename(word) in WRAPPERS:
            i += 1
            while i < len(seg) and seg[i].startswith("-"):  # wrapper options, e.g. env -i
                i += 1
        else:
            break
    return seg[i:]


def current_branch(cwd):
    try:
        out = subprocess.run(["git", "-C", cwd, "rev-parse", "--abbrev-ref", "HEAD"],
                             capture_output=True, text=True, timeout=5)
        return out.stdout.strip() if out.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def check_hooks_path_config(key_value):
    key, _, value = key_value.partition("=")
    if key.lower() == "core.hookspath" and value != ALLOWED_HOOKS_PATH:
        return "overrides core.hooksPath, which disables the repo's git hooks (.githooks/)"
    return None


def check_git(words, cwd):
    i = 1
    while i < len(words) and words[i].startswith("-"):
        opt = words[i]
        name, eq, value = opt.partition("=")
        if name in ("-c", "--config-env") and not eq and i + 1 < len(words):
            reason = check_hooks_path_config(words[i + 1])
            if reason:
                return reason
        if name == "-C" and not eq and i + 1 < len(words):
            cwd = os.path.join(cwd, words[i + 1])
        i += 2 if (name in GIT_VALUE_OPTS and not eq) else 1
    if i >= len(words):
        return None
    sub, args = words[i], words[i + 1:]

    if "--no-verify" in args:
        return "--no-verify skips the repo's git hooks"

    if sub == "commit":
        j = 0
        while j < len(args):
            a = args[j]
            if a == "--":
                break
            if a in COMMIT_VALUE_OPTS:
                j += 2
                continue
            if a.startswith("-") and not a.startswith("--"):
                for ch in a[1:]:
                    if ch == "n":
                        return "git commit -n skips the repo's git hooks"
                    if ch in COMMIT_SHORT_WITH_VALUE:
                        break
            j += 1

    if sub == "config":
        lowered = [a.lower() for a in args]
        if "core.hookspath" in lowered:
            k = lowered.index("core.hookspath")
            if any(a.startswith("--unset") for a in args):
                return "unsets core.hooksPath, which disables the repo's git hooks (.githooks/)"
            if k + 1 < len(args) and args[k + 1] != ALLOWED_HOOKS_PATH:
                return "overrides core.hooksPath, which disables the repo's git hooks (.githooks/)"

    if sub == "push":
        positional = []
        j = 0
        while j < len(args):
            a = args[j]
            if a in ("--all", "--mirror"):
                return f"git push {a} pushes {MAIN}"
            if a in PUSH_VALUE_OPTS:
                j += 2
                continue
            if not a.startswith("-"):
                positional.append(a)
            j += 1
        refspecs = positional[1:]
        for spec in refspecs:
            dst = spec.lstrip("+").rsplit(":", 1)[-1]
            if dst in (MAIN, f"refs/heads/{MAIN}"):
                return f"pushes to {MAIN}; changes reach {MAIN} only through a pull request"
        if not refspecs and current_branch(cwd) == MAIN:
            return f"pushes from {MAIN}; changes reach {MAIN} only through a pull request"
    return None


def check_gh(words):
    args = words[1:]
    if args[:2] == ["pr", "merge"]:
        return "gh pr merge merges a pull request; a human merges after review"
    if args[:1] == ["api"] and any(re.search(r"pulls/[^/\s]+/merge\b", a) for a in args[1:]):
        return "gh api .../merge merges a pull request; a human merges after review"
    return None


def check(command, cwd, depth=0):
    for seg in segments(command):
        words = unwrap(seg)
        if not words:
            continue
        name = os.path.basename(words[0])
        if name in SHELLS and depth < 3 and "-c" in words[1:]:
            k = words.index("-c", 1)
            if k + 1 < len(words):
                reason = check(words[k + 1], cwd, depth + 1)
                if reason:
                    return reason
            continue
        if name == "eval" and depth < 3:
            reason = check(" ".join(words[1:]), cwd, depth + 1)
        elif name == "git":
            reason = check_git(words, cwd)
        elif name == "gh":
            reason = check_gh(words)
        else:
            reason = None
        if reason:
            return reason
    return None


def command_from(payload):
    for key in ("tool_input", "toolInput"):
        tool_input = payload.get(key)
        if isinstance(tool_input, dict) and "command" in tool_input:
            cmd = tool_input["command"]
            return shlex.join(cmd) if isinstance(cmd, list) else cmd
    cmd = payload.get("command")
    return shlex.join(cmd) if isinstance(cmd, list) else cmd


def main(argv):
    adapter = argv[argv.index("--adapter") + 1] if "--adapter" in argv[:-1] else None
    try:
        payload = json.load(sys.stdin)
    except (ValueError, OSError):
        print("guard-git: could not read the hook input; allowing", file=sys.stderr)
        return 0
    if not isinstance(payload, dict):
        return 0
    if adapter == "claude" and "cursor_version" in payload:
        return 0  # Cursor running the imported Claude Code hook; .cursor/hooks.json covers it
    command = command_from(payload)
    if not isinstance(command, str) or not command.strip():
        return 0
    cwd = payload.get("cwd") if isinstance(payload.get("cwd"), str) else os.getcwd()
    reason = check(command, cwd)
    if reason:
        print(f"Blocked by .agents/hooks/guard-git.py: {reason}. "
              "See .ai/rules/agent-workflow.md, \"Validation and Enforcement\".", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
