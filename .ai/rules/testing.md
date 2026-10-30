# Testing Standard

Which test framework each part of the repo uses, and where tests live. This applies the same way to tests written by people and by agents. The .NET part is enforced by tests (see Enforcement), so a test project that breaks it fails `dotnet test`.

## 1. .NET (`src/Rva.Backend`, `src/Rva.Simulator`)

* **Framework:** xUnit v3 (`xunit.v3`) on **Microsoft.Testing.Platform** (MTP). The VSTest stack is not used: no `xunit` (v2), `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, or `coverlet.collector`. No NUnit, MSTest, or TUnit.
* **Opt-in:** the root `global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`, which switches `dotnet test` to MTP for the whole repo. Commands are in `.ai/scripts/verify.md`.
* **Test projects:**
  * live under `tests/`, named `Rva.<Thing>.Tests` (the existing `tests/Rva.ArchitectureTests` keeps its name);
  * set `<OutputType>Exe</OutputType>`; MTP test projects are executables;
  * reference `xunit.v3`, and add `<Using Include="Xunit" />`;
  * are added to `VppAggregator.slnx` (`ProjectReferenceTests` checks this).
* **Approved test packages:** these are the only test packages in the root `Directory.Packages.props`. Adding another one is a decision for a human, recorded here first.

  | Package | Use |
  |---|---|
  | `xunit.v3` | the test framework and its MTP runner |
  | `TngTech.ArchUnitNET.xUnitV3` | architecture rules on compiled assemblies (`tests/Rva.ArchitectureTests`) |
  | `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory<Program>`: starts the real host in memory (`tests/Rva.Host.Tests`) |
  | `Microsoft.Testing.Extensions.CodeCoverage` | code coverage (`--coverage`) |

* **Package versions:** every version is pinned in the root `Directory.Packages.props`, the only such file in the repo. `PackageReference` items carry no `Version` or `VersionOverride`; restore rejects `VersionOverride` (`CentralPackageVersionOverrideEnabled` is false). No file may set `ManagePackageVersionsCentrally` to false.
* **Cancellation:** pass `TestContext.Current.CancellationToken` to async calls that accept a token. The xUnit analyzer (xUnit1051) warns otherwise, and the build should have no warnings.
* **Waiting for async work:** await a `TaskCompletionSource` with a timeout (`WaitAsync(timeout, token)`), never `Task.Delay` or `Thread.Sleep`.

## 2. Database (`infrastructure/postgres`)

pgTAP, run by `infrastructure/postgres/tests/run_tests.sh`. The conventions (one file per scenario, fixtures, TODO tests for known gaps, test-first schema changes) are in `.ai/rules/sql-ddl.md` section 7.

## 3. Python sidecar (`src/Rva.Intelligence`)

pytest, with `pytest-asyncio` for async code (both in `requirements-dev.txt`). Not `unittest` test classes, nose, or other runners. Not enforced yet: the sidecar has no test tooling configured, so enforcement is added with it.

## 4. Not decided yet

These have not been chosen. Do not add a package for them without a human decision recorded here:

* **Test doubles:** hand-written fakes or a mocking library. The existing guidance for domain rules says a fake `IGridTopologyQueries` (`docs/architecture/model-validation-scenarios.md`).
* **Assertion library:** use xUnit's `Assert` until one is chosen.
* **Database integration tests from .NET:** Testcontainers or the compose stack. Needed in Phase 1 Step 1, when the first module gets a `DbContext`.
* **Project layout per module:** one test project per module, or shared ones.

## Enforcement

`tests/Rva.ArchitectureTests` checks every `.csproj`, `.props`, and `.targets` file in the repo:

* `TestFrameworkTests` applies the rules in `Support/TestFrameworkRules.cs` to the real files, and checks that `global.json` opts into MTP.
* `TestFrameworkRulesTests` proves each rule rejects its forbidden shape.

The rules:

* there is exactly one `Directory.Packages.props`, at the root;
* central package management is never switched off, and no package reference carries an inline version;
* no forbidden package (the VSTest stack, other frameworks) appears anywhere;
* any project that references a test package or sets `IsTestProject` lives under `tests/`, references `xunit.v3`, and references **only** the approved test packages above. A mocking library, assertion library, or Testcontainers package therefore fails until it is approved and added to the table and to `TestFrameworkRules.ApprovedTestPackages`. Without the `global.json` opt-in, `dotnet test` stops with an SDK error rather than running zero tests.

These checks run in two places. The local hook gives early feedback, but anyone can skip it. Enforcement happens on the server:

* **on every pull request and every push to `main`**, in the `test-guardrail` GitHub Actions workflow (`.github/workflows/test-guardrail.yml`). This run can't be skipped locally. It is meant to become a required check on `main`, but that has to wait: the repository is private on GitHub Free, which does not enforce rulesets (`.ai/rules/agent-workflow.md`). Until then, a failing run is visible on the pull request but does not block the merge. Do not merge a pull request with a failing `test-guardrail` run;
* **before every commit** that touches a `.csproj`, `.props`, or `.targets` file, `global.json`, or `tests/Rva.ArchitectureTests`. The shared `pre-commit` hook in `.githooks/` runs `TestFrameworkTests` and blocks the commit if it fails. It also refuses to run while any of those files has unstaged or untracked changes, because the tests read the working tree, not the staged content. The hooks are enabled once per clone (`.ai/scripts/verify.md`), and must never be skipped with `--no-verify`;
* whenever `dotnet test` runs, as part of the architecture test suite.
