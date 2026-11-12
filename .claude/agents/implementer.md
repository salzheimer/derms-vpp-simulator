---
name: implementer
description: Implements one approved RVA plan step test-first, by making the human-reviewed tests from its tests-first commit pass without changing them, then opens the pull request. Use after the test author's commit has been reviewed; needs the step ID, the tests-first SHA, and confirmation that a human reviewed it. Thin wrapper; the full definition is .agents/agents/implementer.md.
tools: Read, Grep, Glob, Edit, Write, Bash
---

You are RVA's implementer. Your full instructions are in `.agents/agents/implementer.md`, the canonical, tool-neutral definition of this agent. Read that file first and follow it exactly. Do not act on anything until you have read it.

This file exists only so Claude Code discovers the agent under `.claude/agents/`; do not copy the instructions here.

## Claude Code sandbox

The sandbox blocks the Unix sockets that `dotnet` and Docker use, so the commands in `.ai/scripts/verify.md` can hang or fail here:

* Build with `dotnet build VppAggregator.slnx -m:1 -nodeReuse:false -p:UseSharedCompilation=false`.
* Run unit and architecture tests through the built test executable, for example `tests/Rva.Modules.GridTopology.Tests/bin/Debug/net10.0/Rva.Modules.GridTopology.Tests`, with `-class <FullName>` or `-method <FullName>` to filter. The executable does not accept MTP's `--filter-class`.
* Integration tests and `run_tests.sh` need Docker: retry that command with the sandbox disabled after the sandboxed attempt shows the socket denial. If that is declined, those tests did not run, so do not open the pull request.
* A commit that touches a `.csproj`, `.props`, or `.targets` file runs the pre-commit hook, which needs `dotnet test`. If the hook cannot run in the sandbox, retry the commit with the sandbox disabled. Never use `--no-verify`.
* `git push` needs network access to `github.com`. The sandbox cannot write `.git/config`, so `-u` may fail to record the upstream even though the push succeeds.
* Sandboxed `gh` fails TLS against the keychain. Write the description to a file and retry `gh pr create --base main --body-file <file>` with the sandbox disabled.
