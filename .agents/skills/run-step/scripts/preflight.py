#!/usr/bin/env python3
"""Preflight for the run-step skill (AI_HARNESS_PLAN.md, ai-10).

Checks that one plan step can start, from the repo's own records (status lives in the plans' "Parts" tables; plan
section 10, question 1, decided 2026-11-15: repo for now). Moving status to a tracker changes only this script.

Checks, each reported as PASS or FAIL:
  1. The step has one canonical row in a "Parts" table under .ai/plans/ (the step plan's row, which has a
     "Depends on" column, when a phase plan lists the step too), and its status is not Done.
  2. Every part it depends on is Done ("Done (PR #n)").
  3. No review item of a part it depends on is still undecided (the decision gate, .ai/rules/agent-workflow.md,
     "Review items"). Decided means keep, change, or information, or open marked non-blocking. An item with no
     Decision column, or with a Decision that is open or a question, counts as undecided: the check fails closed.
  4. The working tree is clean.
  5. The branch: on step/<id>-<slug>, or no such branch exists yet (the skill then creates it from origin/main).
     A step branch must contain origin/main (rebase onto it; never merge main in).
It also reports the stage: whether the branch already has a "<id>: test spec" commit, and whether any stub is left.

It cannot check that a human approved the step; the skill asks for that explicitly.

Usage: preflight.py <step-id> [--plans-dir .ai/plans] [--base origin/main]
Exit status: 0 when every check passes, 1 when any fails, 2 on usage errors.
"""

import os
import re
import subprocess
import sys

STEP_ID = re.compile(r"\b(?:ai-\d+|p\d+-s\d+[a-z]?)\b")
DECIDED = re.compile(r"^`?(keep|change|information|open \(non-blocking)", re.IGNORECASE)


def git(*args):
    out = subprocess.run(["git", *args], capture_output=True, text=True)
    return out.returncode, out.stdout.strip()


def cells(line):
    """Split a Markdown table row into cells; pipes inside backticks or escaped (\\|) do not split."""
    line = line.strip()
    if not (line.startswith("|") and line.endswith("|")):
        return None
    parts, cur, tick, prev = [], "", False, ""
    for ch in line[1:-1]:
        if ch == "`":
            tick = not tick
        if ch == "|" and not tick and prev != "\\":
            parts.append(cur.strip())
            cur = ""
        else:
            cur += ch
        prev = ch
    parts.append(cur.strip())
    return parts


def tables(path):
    """Yield (heading, header, rows) for every Markdown table in the file."""
    heading, header, rows = "", None, []
    lines = open(path, encoding="utf-8").read().splitlines() + [""]
    for line in lines:
        c = cells(line)
        if c is None:
            if header is not None:
                yield heading, header, rows
            header, rows = None, []
            if line.startswith("#"):
                heading = line.lstrip("#").strip()
            continue
        if header is None:
            header = [h.strip("* ") for h in c]
        elif all(re.fullmatch(r":?-+:?", x) for x in c if x):
            continue
        else:
            rows.append(dict(zip(header, c)))


def strip_ticks(s):
    return s.replace("`", "").strip()


DEP = re.compile(r"(ai-\d+|p\d+-s\d+[a-z]?|s\d+[a-z]?)(?:\s*(?:–|\.\.)\s*(ai-\d+|p\d+-s\d+[a-z]?|s\d+[a-z]?))?")


def expand_deps(text, own_id):
    """Parse a 'Depends on' cell: `ai-04`, `ai-04`–`ai-06`, s01c, s01a, s01b (note), or —."""
    phase = own_id.split("-s")[0] if re.match(r"p\d+-s", own_id) else None

    def full(token):
        return f"{phase}-{token}" if token.startswith("s") and phase else token

    deps = []
    for m in DEP.finditer(strip_ticks(text)):
        start, end = full(m.group(1)), m.group(2) and full(m.group(2))
        if end and start.startswith("ai-") and end.startswith("ai-"):
            deps += [f"ai-{n:02d}" for n in range(int(start[3:]), int(end[3:]) + 1)]
        else:
            deps += [start] + ([end] if end else [])
    return [d for d in dict.fromkeys(deps) if d != own_id]


def find_parts(plans_dir):
    parts = {}
    for name in sorted(os.listdir(plans_dir)):
        if not name.endswith(".md"):
            continue
        path = os.path.join(plans_dir, name)
        for _, header, rows in tables(path):
            if not header or header[0] != "Part":
                continue
            for row in rows:
                pid = strip_ticks(row.get("Part", ""))
                if STEP_ID.fullmatch(pid):
                    parts.setdefault(pid, []).append((path, row))
    return parts


def review_items(plans_dir):
    """Yield (path, item_id, part_ids, decision or None) for every review-item table row."""
    for name in sorted(os.listdir(plans_dir)):
        if not name.endswith(".md"):
            continue
        path = os.path.join(plans_dir, name)
        for heading, header, rows in tables(path):
            if not header or header[0] not in ("#", "ID"):
                continue
            is_review = any(h.startswith("Decision") for h in header) or re.search(r"for review|review items", heading, re.I)
            if not is_review:
                continue
            for row in rows:
                item = strip_ticks(row.get(header[0], ""))
                if not re.fullmatch(r"[A-Z]\d+", item):
                    continue
                if "Part" in row:
                    owners = STEP_ID.findall(strip_ticks(row["Part"]))
                else:
                    owners = STEP_ID.findall(strip_ticks(heading))
                decision = row.get("Decision")
                yield path, item, owners, decision


def canonical(rows):
    """The step plan's row (it has "Depends on") when a phase plan lists the step too."""
    with_deps = [r for r in rows if "Depends on" in r[1]]
    return with_deps if len(rows) > 1 and len(with_deps) == 1 else rows


def main(argv):
    if not argv or argv[0].startswith("-"):
        print(__doc__.strip().splitlines()[-3], file=sys.stderr)
        return 2
    step = argv[0]
    plans_dir = argv[argv.index("--plans-dir") + 1] if "--plans-dir" in argv else ".ai/plans"
    base = argv[argv.index("--base") + 1] if "--base" in argv else "origin/main"
    results = []

    def report(ok, what):
        results.append(ok)
        print(f"{'PASS' if ok else 'FAIL'}  {what}")

    print(f"run-step preflight: {step}")
    parts = find_parts(plans_dir)
    rows = parts.get(step, [])
    # A phase plan may list the step too; the step plan's row (the one with "Depends on") is canonical (AGENTS.md).
    chosen = canonical(rows)
    if len(rows) > 1 and len(chosen) == 1:
        for other_path, other in rows:
            if other is not chosen[0][1]:
                other_status = strip_ticks(other.get("Status", ""))
                same = other_status.split(" (")[0] == strip_ticks(chosen[0][1].get("Status", "")).split(" (")[0]
                if not other_status.startswith("See "):  # a pointer to the step plan, not a status
                    print(f"INFO  also listed in {other_path}, status \"{other_status}\"" + ("" if same else " (differs; the step plan's row is canonical)"))
        rows = chosen
    if len(rows) != 1:
        where = ", ".join(p for p, _ in rows) or "no Parts table"
        report(False, f"step row: expected one row for {step} in a Parts table, found {len(rows)} ({where})")
        return 1
    path, row = rows[0]
    status = strip_ticks(row.get("Status", ""))
    report(not status.startswith("Done"), f"step row: {path}, status \"{status}\"")

    deps = expand_deps(row.get("Depends on", ""), step)
    if not deps:
        report(True, "dependencies: none")
    for dep in deps:
        dep_rows = canonical(parts.get(dep, []))
        dep_status = strip_ticks(dep_rows[0][1].get("Status", "")) if len(dep_rows) == 1 else "not found"
        report(dep_status.startswith("Done"), f"dependency {dep}: \"{dep_status}\"")

    blocking = [(p, i, d) for p, i, owners, d in review_items(plans_dir)
                if any(o in deps for o in owners) and not (d is not None and DECIDED.match(strip_ticks(d)))]
    if blocking:
        for p, item, d in blocking:
            shown = "no Decision column" if d is None else f"Decision \"{strip_ticks(d)[:60]}\""
            report(False, f"review item {item} ({os.path.basename(p)}): {shown}; decide it, or mark it open (non-blocking, <who>, <date>)")
    else:
        report(True, "review items of the dependencies: all decided")

    code, dirty = git("status", "--porcelain")
    report(code == 0 and dirty == "", "working tree: clean" if not dirty else "working tree: not clean (commit or stash first)")

    _, branch = git("rev-parse", "--abbrev-ref", "HEAD")
    _, existing = git("for-each-ref", "--format=%(refname:short)", f"refs/heads/step/{step}-*")
    existing = [b for b in existing.splitlines() if b]
    if branch.startswith(f"step/{step}-"):
        contains = subprocess.run(["git", "merge-base", "--is-ancestor", base, "HEAD"]).returncode == 0
        report(contains, f"branch: {branch}" + ("" if contains else f" does not contain {base}; rebase onto it"))
        _, log = git("log", "--first-parent", "--format=%h %s", f"{base}..HEAD")
        spec = [l for l in log.splitlines() if l.endswith(f"{step}: test spec")]
        _, stubs = git("grep", "-l", "-E", "NotImplemented(Exception|Error)", "--", "src")
        stage = "test spec written: " + spec[0].split()[0] if spec else "no test spec commit yet"
        print(f"INFO  stage: {stage}; files with stubs under src/: {len(stubs.splitlines()) if stubs else 0}")
    elif existing:
        report(False, f"branch: on {branch}, but {', '.join(existing)} exists; switch to it")
    else:
        report(True, f"branch: none yet; create step/{step}-<slug> from {base} (git switch --no-track -c ...)")

    ok = all(results)
    print(f"run-step preflight: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
