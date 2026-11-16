#!/usr/bin/env python3
"""Render the handoff the run-step skill gives an agent (AI_HARNESS_PLAN.md, ai-10).

  handoff.py test-author <step-id>
  handoff.py implementer <step-id> --spec <sha> --confirmation "<the human's words>"

The handoff names the step, its plan, the step's row (scope, dependencies, acceptance criteria), and the plan sections
whose heading names the step (for example its test catalogue subsection), so the agent starts from the same inputs
every time. The agent definitions (.agents/agents/) say what to do with them.

The implementer handoff is refused unless:
  * --spec names a commit on the current branch titled "<step-id>: test spec" (or the latest "<step-id>: spec
    correction" after it), and
  * --confirmation is the human's own statement that they reviewed that commit against the acceptance criteria.
The script cannot tell who typed the confirmation; the skill passes it only from a human message
(.agents/skills/run-step/SKILL.md, step 4).

Exit status: 0 with the handoff on stdout, 1 when refused, 2 on usage errors.
"""

import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preflight  # noqa: E402


def step_sections(path, step):
    """Headings in the plan that name the step (backticked or bare), with their text up to the next same-level one."""
    lines = open(path, encoding="utf-8").read().splitlines()
    out, i = [], 0
    while i < len(lines):
        m = re.match(r"^(#+)\s+(.*)", lines[i])
        if m and re.search(rf"(?<![\w-]){re.escape(step)}(?![\w-])", m.group(2)):
            level, start, i = len(m.group(1)), i, i + 1
            while i < len(lines):
                n = re.match(r"^(#+)\s", lines[i])
                if n and len(n.group(1)) <= level:
                    break
                i += 1
            out.append("\n".join(lines[start:i]).rstrip())
        else:
            i += 1
    return out


def step_row(step, plans_dir=".ai/plans"):
    rows = preflight.canonical(preflight.find_parts(plans_dir).get(step, []))
    if len(rows) != 1:
        sys.exit(f"handoff: expected one row for {step} in a Parts table, found {len(rows)}")
    return rows[0]


def common(step):
    path, row = step_row(step)
    _, branch = preflight.git("rev-parse", "--abbrev-ref", "HEAD")
    parts = [f"Plan step: `{step}`", f"Plan: `{path}`", f"Branch: `{branch}`", "",
             "The step's row in the plan's Parts table:", ""]
    parts += [f"- **{k}:** {v}" for k, v in row.items()]
    sections = step_sections(path, step)
    if sections:
        parts += ["", "Plan sections that name this step (read the whole plan too, for its decisions and contracts):", ""]
        parts += sections
    return parts


def test_author(step):
    lines = [f"You are the test author for `{step}`. Follow `.agents/agents/test-author.md`.",
             "The step is approved by a human; the run-step preflight passed.", ""]
    lines += common(step)
    lines += ["", "Find every other input by role (`.ai/rules/agent-workflow.md`, \"What the step's plan gives the agents\").",
              f"Commit the tests and stubs on this branch as `{step}: test spec`. Do not push. Hand off as the definition says."]
    return "\n".join(lines)


def implementer(step, spec, confirmation):
    if not confirmation or len(confirmation.strip()) < 10:
        sys.exit("handoff: refused. --confirmation must be the human's statement that they reviewed the spec commit.")
    code, subject = preflight.git("log", "-1", "--format=%s", spec)
    if code != 0:
        sys.exit(f"handoff: refused. {spec} is not a commit.")
    if subject not in (f"{step}: test spec", f"{step}: spec correction"):
        sys.exit(f"handoff: refused. {spec} is \"{subject}\", not \"{step}: test spec\" or \"{step}: spec correction\".")
    if subprocess.run(["git", "merge-base", "--is-ancestor", spec, "HEAD"]).returncode != 0:
        sys.exit(f"handoff: refused. {spec} is not on the current branch.")
    _, later = preflight.git("log", "--first-parent", "--format=%h %s", f"{spec}..HEAD")
    newer = [l for l in later.splitlines() if l.endswith(f"{step}: spec correction")]
    if newer:
        sys.exit(f"handoff: refused. A later correction ({newer[0]}) needs its own review; pass that commit.")
    _, short = preflight.git("rev-parse", "--short", spec)
    lines = [f"You are the implementer for `{step}`. Follow `.agents/agents/implementer.md`.", "",
             f"Spec commit: `{short}` (`{subject}`)",
             f"Human confirmation, verbatim: \"{confirmation.strip()}\"", ""]
    lines += common(step)
    lines += ["", "Use the test author's handoff for the stub list and the shapes the human confirmed.",
              "Open the pull request only when every reviewed test passes; never merge."]
    return "\n".join(lines)


def main(argv):
    if len(argv) < 2 or argv[0] not in ("test-author", "implementer"):
        print(__doc__.strip().splitlines()[2] + "\n" + __doc__.strip().splitlines()[3], file=sys.stderr)
        return 2
    role, step = argv[0], argv[1]
    if role == "test-author":
        print(test_author(step))
        return 0
    spec = argv[argv.index("--spec") + 1] if "--spec" in argv[:-1] else None
    confirmation = argv[argv.index("--confirmation") + 1] if "--confirmation" in argv[:-1] else None
    if not spec:
        print("handoff: --spec <sha> is required for the implementer", file=sys.stderr)
        return 2
    print(implementer(step, spec, confirmation))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
