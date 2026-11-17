#!/usr/bin/env python3
"""Select, run, and report the checks a change needs (AI_HARNESS_PLAN.md, ai-12; commands from the verify skill).

  select-checks.py [--base <ref>] [--head <ref>] [--worktree] [--body-file <file>] [--dry-run]

Changed paths are `git diff --name-only --no-renames` from merge-base(base, head) to head (base defaults to
origin/main, head to HEAD). Deleted and moved files count by their old and new paths. --worktree also counts
uncommitted and untracked files. Each check in CHECKS says which paths select it; the rules mirror the CI path
filters (.github/workflows/), so a path selects locally what it triggers in CI.

The report (Markdown, on stdout) lists every check as passed, failed, not run (with the reason), or not selected
(with the paths that would select it), then any changed path no check covers. Handoffs and pull requests copy it
into "Validation". Each check's output goes to <tmp>/select-checks/<id>.log; a failure prints the log's tail on
stderr.

--dry-run selects and reports without running anything; it reads git only.
--body-file passes a pull request description to the contract check, which needs one.

Exit status: 0 when every selected check passed (or, with --dry-run, always), 1 when any failed, 3 when none failed
but a selected check did not run (missing tool, Docker unreachable, timed out, no --body-file), 2 on usage errors.
A check that cannot exist yet (the sidecar's pytest and ruff) is reported but does not change the exit status.
Standard library only; Python 3.8+.
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time

MSBUILD_SUFFIXES = (".csproj", ".props", ".targets")
DOTNET_ROOT_FILES = ("global.json", "Directory.Packages.props", "VppAggregator.slnx")


def under(path, *prefixes):
    return any(path == p.rstrip("/") or path.startswith(p) for p in prefixes)


def is_msbuild(path):
    return path.endswith(MSBUILD_SUFFIXES) or path in ("global.json", "Directory.Packages.props") \
        or os.path.basename(path).startswith("Directory.Build.")


def is_dotnet(path):
    return under(path, "src/Rva.Backend/", "src/Rva.Simulator/", "tests/") or path in DOTNET_ROOT_FILES \
        or is_msbuild(path) or path == ".github/workflows/ci.yml"


def is_postgres(path):
    return under(path, "infrastructure/postgres/")


def is_contracts(path):
    return path.startswith("src/") and ".Contracts/" in path


HARNESS_WF = ".github/workflows/harness.yml"


class Check:
    def __init__(self, cid, name, command, selects, why, needs=(), timeout=600, after=None, unavailable=None,
                 always=False):
        self.id, self.name, self.command = cid, name, command
        self.selects, self.why = selects, why
        self.needs, self.timeout, self.after = needs, timeout, after
        self.unavailable, self.always = unavailable, always


# Order matters: a check with `after` runs only when that check passed.
CHECKS = [
    Check("dotnet-build", ".NET build", "dotnet build VppAggregator.slnx",
          is_dotnet, "src/Rva.Backend/, src/Rva.Simulator/, tests/, MSBuild files, global.json, the solution, ci.yml",
          needs=("dotnet",), timeout=900),
    Check("dotnet-test", ".NET tests, all", "dotnet test --solution VppAggregator.slnx --no-build",
          is_dotnet, "src/Rva.Backend/, src/Rva.Simulator/, tests/, MSBuild files, global.json, the solution, ci.yml", needs=("dotnet", "docker"), timeout=1800, after="dotnet-build"),
    Check("integration-tests", "GridTopology integration tests",
          "dotnet test --project tests/Rva.Modules.GridTopology.Integration.Tests",
          is_postgres, "infrastructure/postgres/ (the tests mount its schema and seed)",
          needs=("dotnet", "docker"), timeout=1800),
    Check("test-guardrail", "Test framework guardrail",
          "dotnet test --project tests/Rva.ArchitectureTests --filter-class Rva.ArchitectureTests.TestFrameworkTests",
          lambda p: is_msbuild(p) or p == ".github/workflows/test-guardrail.yml",
          "MSBuild files, global.json, Directory.Packages.props, test-guardrail.yml", needs=("dotnet",), timeout=900),
    Check("db-tests", "Schema lint and pgTAP", "infrastructure/postgres/tests/run_tests.sh",
          lambda p: is_postgres(p) or p in ("docs/architecture/model-validation-scenarios.md",
                                            ".github/workflows/db-tests.yml"),
          "infrastructure/postgres/, model-validation-scenarios.md, db-tests.yml (the db-tests path filter)",
          needs=("docker",), timeout=900),
    Check("image-backend", "Backend image", "docker build -f src/Rva.Backend/Rva.Host/Dockerfile -t rva-backend .",
          lambda p: under(p, "src/Rva.Backend/") or p in ("global.json", "Directory.Packages.props",
                                                         ".github/workflows/ci.yml"),
          "src/Rva.Backend/, global.json, Directory.Packages.props, ci.yml", needs=("docker",), timeout=900),
    Check("image-simulator", "Simulator image", "docker build -f src/Rva.Simulator/Dockerfile -t rva-simulator .",
          lambda p: under(p, "src/Rva.Simulator/") or p in ("global.json", "Directory.Packages.props",
                                                           ".github/workflows/ci.yml"),
          "src/Rva.Simulator/, global.json, Directory.Packages.props, ci.yml", needs=("docker",), timeout=900),
    Check("image-intelligence", "Sidecar image", "docker build -t rva-intelligence src/Rva.Intelligence",
          lambda p: under(p, "src/Rva.Intelligence/") or p == ".github/workflows/ci.yml",
          "src/Rva.Intelligence/, ci.yml", needs=("docker",), timeout=900),
    Check("sidecar-tests", "Sidecar tests and lint", "pytest && ruff check .",
          lambda p: under(p, "src/Rva.Intelligence/"), "src/Rva.Intelligence/",
          unavailable="not configured yet (verify skill, \"Python\")"),
    Check("references", "Harness references", ".agents/skills/verify/scripts/check-references.sh",
          lambda p: True, "every change (a moved or renamed file can break a named path)", needs=("bash", "git"),
          timeout=120, always=True),
    Check("hooks", "Agent hooks", ".agents/hooks/tests/test-guard-git.sh",
          lambda p: under(p, ".agents/hooks/") or p in (".claude/settings.json", ".codex/hooks.json",
                                                       ".cursor/hooks.json", HARNESS_WF),
          ".agents/hooks/, the hook adapters, harness.yml", needs=("bash", "git", "python3"), timeout=120),
    Check("run-step", "run-step scripts", ".agents/skills/run-step/scripts/test-run-step.sh",
          lambda p: under(p, ".agents/skills/run-step/") or p == HARNESS_WF,
          ".agents/skills/run-step/, harness.yml", needs=("bash", "git", "python3"), timeout=120),
    Check("select-checks", "Check selection", ".agents/skills/verify/scripts/test-select-checks.sh",
          lambda p: p in (".agents/skills/verify/scripts/select-checks.py",
                          ".agents/skills/verify/scripts/test-select-checks.sh", HARNESS_WF),
          "select-checks.py, its tests, harness.yml", needs=("bash", "git", "python3"), timeout=120),
    Check("contracts", "Contract changes",
          ".agents/skills/verify/scripts/check-contract-changes.sh --body-file {body} --base {base} --head {head}",
          lambda p: is_contracts(p) or (p.startswith("src/") and p.endswith(".cs")),
          "a .Contracts file, or C# under src/ (routes)", needs=("bash", "git", "perl"), timeout=120),
    Check("test-spec", "Test spec", ".agents/skills/verify/scripts/check-test-spec.sh --base {base} --head {head}",
          None, "a \"<id>: test spec\" commit in the range", needs=("bash", "git"), timeout=120),
]


def git(*args):
    r = subprocess.run(["git", *args], capture_output=True, text=True)
    return r.returncode, r.stdout.strip(), r.stderr.strip()


def changed_paths(base, head, worktree):
    code, mb, err = git("merge-base", base, head)
    if code != 0:
        sys.exit(f"select-checks: no merge base for {base} and {head}: {err}")
    args = ["diff", "--name-only", "--no-renames", mb] + ([] if worktree else [head])
    _, out, _ = git(*args)
    paths = set(out.splitlines())
    if worktree:
        _, untracked, _ = git("ls-files", "--others", "--exclude-standard")
        paths |= set(untracked.splitlines())
    return mb, sorted(p for p in paths if p)


def has_spec_commit(mb, head):
    _, out, _ = git("log", "--first-parent", "--format=%s", f"{mb}..{head}")
    return any(s.endswith(": test spec") for s in out.splitlines())


def docker_reachable():
    if not shutil.which("docker"):
        return False
    try:
        return subprocess.run(["docker", "info"], capture_output=True, timeout=30).returncode == 0
    except (subprocess.TimeoutExpired, OSError):
        return False


def summarize(paths, limit=3):
    shown = ", ".join(f"`{p}`" for p in paths[:limit])
    return shown + (f" and {len(paths) - limit} more" if len(paths) > limit else "")


def run(check, command, logdir):
    log = os.path.join(logdir, check.id + ".log")
    start = time.time()
    with open(log, "w") as fh:
        try:
            code = subprocess.run(command, shell=True, stdout=fh, stderr=subprocess.STDOUT,
                                  timeout=check.timeout).returncode
        except subprocess.TimeoutExpired:
            return "not run", f"timed out after {check.timeout} s (log `{log}`)"
    secs = int(time.time() - start)
    if code == 0:
        return "passed", f"{secs} s"
    with open(log) as fh:
        tail = fh.read().splitlines()[-25:]
    print(f"--- {check.name} failed (exit {code}); last lines of {log}:", file=sys.stderr)
    print("\n".join(tail), file=sys.stderr)
    return "failed", f"exit {code}, {secs} s (log `{log}`)"


def main(argv):
    ap = argparse.ArgumentParser(add_help=True, description=__doc__.strip().splitlines()[0])
    ap.add_argument("--base", default="origin/main")
    ap.add_argument("--head", default="HEAD")
    ap.add_argument("--worktree", action="store_true")
    ap.add_argument("--body-file")
    ap.add_argument("--dry-run", action="store_true")
    try:
        a = ap.parse_args(argv)
    except SystemExit as e:
        return 2 if e.code else 0
    if a.body_file and not os.path.isfile(a.body_file):
        print(f"select-checks: --body-file {a.body_file} not found", file=sys.stderr)
        return 2
    code, top, _ = git("rev-parse", "--show-toplevel")
    if code != 0:
        print("select-checks: not in a git repository", file=sys.stderr)
        return 2
    os.chdir(top)

    mb, paths = changed_paths(a.base, a.head, a.worktree)
    spec = has_spec_commit(mb, a.head)
    selected = {}
    for c in CHECKS:
        hits = [p for p in paths if c.selects(p)] if c.selects else []
        if c.id == "test-spec" and spec:
            hits = ["(spec commit)"]
        if c.always or hits:
            selected[c.id] = hits
    covered = {p for c in CHECKS if c.selects and not c.always for p in paths if c.selects(p)}
    uncovered = [p for p in paths if p not in covered]

    logdir = os.path.join(tempfile.gettempdir(), "select-checks")
    os.makedirs(logdir, exist_ok=True)
    docker = None
    results = {}
    for c in CHECKS:
        if c.id not in selected:
            continue
        if c.id == "integration-tests" and "dotnet-test" in selected:
            results[c.id] = ("covered", "by .NET tests, all")
            continue
        if c.unavailable:
            results[c.id] = ("not available", c.unavailable)
            continue
        command = c.command.format(base=a.base, head=a.head, body=a.body_file or "")
        if c.id == "contracts" and not a.body_file:
            results[c.id] = ("not run", "needs the pull request description: pass --body-file")
            continue
        if a.dry_run:
            results[c.id] = ("selected", "dry run")
            continue
        if c.after and results.get(c.after, ("",))[0] != "passed":
            results[c.id] = ("not run", f"{dict((x.id, x.name) for x in CHECKS)[c.after]} did not pass")
            continue
        missing = [t for t in c.needs if t != "docker" and not shutil.which(t)]
        if missing:
            results[c.id] = ("not run", f"{', '.join(missing)} not found")
            continue
        if "docker" in c.needs:
            if docker is None:
                docker = docker_reachable()
            if not docker:
                results[c.id] = ("not run", "Docker is not reachable (`docker info` failed)")
                continue
        results[c.id] = run(c, command, logdir)

    head_short = git("rev-parse", "--short", a.head)[1]
    scope = f"`{mb[:7]}..{head_short}` (merge base with `{a.base}`)" + (", plus uncommitted and untracked files"
                                                                         if a.worktree else "")
    print("### Checks (`select-checks.py`" + (", dry run" if a.dry_run else "") + ")\n")
    print(f"Changed paths: {len(paths)}, in {scope}.\n")
    print("| Check | Command | Selected by | Result |")
    print("|---|---|---|---|")
    for c in CHECKS:
        if c.id not in results:
            continue
        status, detail = results[c.id]
        by = "every change" if c.always else summarize(selected[c.id]) if selected[c.id] != ["(spec commit)"] \
            else "a `test spec` commit in the range"
        cmd = c.command.format(base=a.base, head=a.head, body=a.body_file or "<description>")
        print(f"| {c.name} | `{cmd}` | {by} | **{status}**: {detail} |")
    rest = [c for c in CHECKS if c.id not in results]
    if rest:
        print("\nNot selected (no changed path selects them):\n")
        for c in rest:
            print(f"* {c.name}: selected by {c.why}")
    if uncovered:
        print("\nNo check covers these changed paths beyond the reference check; review them by hand:\n")
        for p in uncovered:
            print(f"* `{p}`")
    print("\nCI still runs `ci`, `harness`, and `test-guardrail` on every pull request; `db-tests` runs on its "
          "path filter. Not selected here means not needed locally, not skipped in CI.")

    if a.dry_run:
        return 0
    statuses = [s for s, _ in results.values()]
    if "failed" in statuses:
        return 1
    if "not run" in statuses:
        return 3
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
