# Rig Guards (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make it impossible for the test rig to silently stop running, and impossible for `main` to stay red without someone being told.

**Architecture:** Four independent guards, no new projects and no new test code. A minimum-test-count floor so a collapse in test discovery fails loudly; an SDK pin so runner-side SDK drift cannot change build behaviour with no repo change; a CI job that opens or updates a single GitHub issue when `main` goes red and closes it on recovery; and `enforce_admins` on branch protection so red commits cannot be merged past the required check.

**Tech Stack:** .NET 10 SDK, Microsoft.Testing.Platform, xunit.v3 4.0.0, GitHub Actions, `gh` CLI.

**Spec:** `docs/superpowers/specs/2026-08-21-testing-rig-hardening-design.md`

## Global Constraints

- Target framework is `net10.0`. Do not change it.
- The required branch-protection status check is named exactly **`build`**. Do not rename, delete, or matrix-ise the `build` job in this phase. Phase 2 introduces the matrix; Phase 1 must leave the check name untouched.
- SDK pin is `10.0.400` with `rollForward: latestPatch` — locks the `10.0.4xx` feature band, still takes patches.
- Minimum-expected-tests floor for `MemoryLens.Mcp.Tests` is `130` (against 137 actual). It is a floor, not an exact count.
- The `global.json` `test.runner` key must be preserved exactly as `Microsoft.Testing.Platform`. Removing it re-breaks `dotnet test` (see #150).
- Alert issue label is `ci-red`. One issue, many comments — never one issue per failure.
- Commit messages follow Conventional Commits; release-please parses them.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj` | Modify | Carries the `--minimum-expected-tests` floor for this test application |
| `global.json` | Modify | Pins the SDK feature band; already carries the MTP runner opt-in |
| `.github/workflows/ci.yml` | Modify | Switches to `global-json-file`; adds the `alert` job and explicit permissions |
| `.github/workflows/release.yml` | Modify | Switches to `global-json-file` |
| `.github/workflows/release-please.yml` | Modify | Switches to `global-json-file` |
| GitHub repo settings | Modify | `ci-red` label; `enforce_admins` on `main` |

Tasks 1–3 are code and land as one PR. Task 4 is a repository settings change applied outside the PR.

## Before you start

Work on a branch off current `main`:

```bash
git fetch origin
git checkout -b ci/rig-guards origin/main
```

Everywhere below that says "the working branch", it means `ci/rig-guards`. The design spec lives on a separate branch (`docs/testing-rig-hardening`) and is not needed to execute this plan.

---

### Task 1: Minimum expected test count

**Files:**
- Modify: `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj:3-7` (the `PropertyGroup`)

**Interfaces:**
- Consumes: nothing.
- Produces: the MSBuild property `TestingPlatformCommandLineArguments`, which Tasks 2–4 do not touch. Phase 2's new test projects each get their own copy of this property with their own floor.

**Background for the implementer:** Microsoft.Testing.Platform reads extra CLI arguments from the `TestingPlatformCommandLineArguments` MSBuild property. `--minimum-expected-tests N` makes the test application exit non-zero if fewer than N tests ran. This guards the dangerous failure mode where test discovery silently breaks and a run reporting "0 tests, success" looks green. The property is **per test application**, not per solution.

- [ ] **Step 1: Write the failing guard — set the floor deliberately too high**

In `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj`, add the last line to the existing `PropertyGroup`:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TestingPlatformCommandLineArguments>--minimum-expected-tests 999</TestingPlatformCommandLineArguments>
  </PropertyGroup>
```

- [ ] **Step 2: Run tests to verify the guard fires**

Run: `dotnet test -c Release`

Expected: FAIL. The distinctive shape is a *platform* error rather than a test failure — note `error: 1` alongside `failed: 0`:

```
Test run summary: Failed!
  error: 1
  failed: 0
Test run completed with non-success exit code: 9
```

If you instead see `Passed!`, the property is not being read — check it is inside a `PropertyGroup` in the **test** project, not `Directory.Build.props`.

- [ ] **Step 3: Set the real floor**

Change `999` to `130`:

```xml
    <TestingPlatformCommandLineArguments>--minimum-expected-tests 130</TestingPlatformCommandLineArguments>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`

Expected: PASS.

```
Test run summary: Passed!
  total: 137
  failed: 0
```

- [ ] **Step 5: Commit**

```bash
git add tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj
git commit -m "test: fail the run if test discovery collapses

Sets a --minimum-expected-tests floor of 130 against 137 actual, so a run
that discovers zero tests fails loudly instead of reporting success. A floor
rather than an exact count, so adding tests never requires a bump."
```

---

### Task 2: SDK pin and deterministic runner install

**Files:**
- Modify: `global.json` (whole file)
- Modify: `.github/workflows/ci.yml:20-22`
- Modify: `.github/workflows/release.yml:18-20`
- Modify: `.github/workflows/release-please.yml:38-40`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: a `global.json` containing both an `sdk` and a `test` section. Task 3 does not modify `global.json`.

**Background for the implementer:** `global.json` currently pins nothing — the runner installs whatever `10.0.x` is newest, so build behaviour can change with no repo commit. `rollForward: latestPatch` locks the feature band (`10.0.4xx`) while still accepting bugfix patches; feature bands are where behaviour changes land. Switching `setup-dotnet` from `dotnet-version` to `global-json-file` is what actually makes CI deterministic — the pin alone does nothing if the runner installs something else and rolls forward.

**Do not drop the `test` section.** Removing it re-breaks `dotnet test` exactly as in #150.

- [ ] **Step 1: Write the failing check — pin to a version that does not exist**

Replace the whole of `global.json` with:

```json
{
  "sdk": {
    "version": "10.0.999",
    "rollForward": "latestPatch"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Step 2: Run the SDK resolver to verify it refuses**

Run: `dotnet --version`

Expected: FAIL — the SDK cannot resolve the pin. The message is confusingly worded but the failure is real:

```
The command could not be loaded, possibly because:
  * You intended to execute a .NET application:
```

This confirms the pin is genuinely being honoured rather than ignored.

- [ ] **Step 3: Set the real pin**

Replace the whole of `global.json` with:

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestPatch"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Step 4: Verify resolution and that tests still run**

Run: `dotnet --version`
Expected: `10.0.400`

Run: `dotnet test -c Release`
Expected: `Test run summary: Passed!` with `total: 137` — proving the `test` section survived the rewrite.

- [ ] **Step 5: Point all three workflows at global.json**

In each of `.github/workflows/ci.yml`, `.github/workflows/release.yml`, and `.github/workflows/release-please.yml`, replace the `with:` block of the `actions/setup-dotnet@v6` step.

Before:

```yaml
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x
```

After:

```yaml
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: ./global.json
```

There are exactly three occurrences, one per file. Verify with `grep -n "dotnet-version" .github/workflows/*.yml` — expected output: no matches.

- [ ] **Step 6: Commit**

```bash
git add global.json .github/workflows/ci.yml .github/workflows/release.yml .github/workflows/release-please.yml
git commit -m "build: pin the SDK feature band and install it from global.json

Runner-side SDK bumps could change build behaviour with no repo change at
all, which is hard to diagnose because git log shows nothing. latestPatch
locks the 10.0.4xx band while still taking patches, and setup-dotnet now
installs from global.json instead of resolving 10.0.x to whatever is newest.

Does not address the #150 break, which came from the xunit v4 bump meeting
an SDK major already in use."
```

---

### Task 3: Red-main alert

**Files:**
- Modify: `.github/workflows/ci.yml` (add top-level `permissions`, add the `alert` job)
- Create: the `ci-red` GitHub label (via `gh`, not a file)

**Interfaces:**
- Consumes: the existing job id `build` in `ci.yml`. Does not rename it.
- Produces: a job id `alert` with `needs: [build]`. **Phase 2 must add `test` to that `needs` list and to the result checks when the matrix job is introduced** — otherwise a failing matrix leg skips `build` and the alert stops firing.

**Background for the implementer:** This is the guard that would actually have prevented the six-day outage — the MTP failure was loud from the first run and nothing was listening.

The condition must **not** be a bare `if: failure()`. When a needed job is skipped rather than failed, a dependent job can be skipped along with it, producing an alert that silently never fires in exactly the case it exists for. Always use `always()` plus explicit `needs.<job>.result` checks.

Branch scoping comes from `on.push.branches`, not from a `github.ref` check in the job condition. That is deliberate: it keeps the job testable by temporarily adding a branch to the trigger (Step 5).

- [ ] **Step 1: Create the label**

Run:

```bash
gh label create ci-red --color b60205 --description "CI is failing on main"
```

Verify: `gh label list | grep ci-red`
Expected: a `ci-red` row. The repo has no such label today.

- [ ] **Step 2: Add explicit permissions and the alert job to ci.yml**

`ci.yml` has no `permissions:` block today and so inherits the repo default. Add an explicit top-level block immediately after the `on:` block and before `jobs:`:

```yaml
permissions:
  contents: read
```

Then append this job at the end of `.github/workflows/ci.yml`, at the same indentation as the existing `build:` job:

```yaml
  alert:
    needs: [build]
    if: always() && github.event_name == 'push'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      issues: write
    env:
      GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
      GH_REPO: ${{ github.repository }}
      SHA: ${{ github.sha }}
      RUN_URL: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
    steps:
      - name: Open or update the red-main issue
        if: needs.build.result == 'failure'
        run: |
          set -euo pipefail
          existing=$(gh issue list --label ci-red --state open --json number --jq '.[0].number // empty')
          body=$(printf 'CI failed on `main` at %s.\n\nRun: %s\n' "$SHA" "$RUN_URL")
          if [ -n "$existing" ]; then
            gh issue comment "$existing" --body "$body"
            echo "Commented on existing issue #$existing"
          else
            gh issue create --title "CI is red on main" --label ci-red --body "$body"
            echo "Opened a new ci-red issue"
          fi

      - name: Close the red-main issue on recovery
        if: needs.build.result == 'success'
        run: |
          set -euo pipefail
          existing=$(gh issue list --label ci-red --state open --json number --jq '.[0].number // empty')
          if [ -n "$existing" ]; then
            gh issue comment "$existing" --body "$(printf 'Recovered on %s.\n\nRun: %s\n' "$SHA" "$RUN_URL")"
            gh issue close "$existing"
            echo "Closed issue #$existing"
          else
            echo "No open ci-red issue; nothing to close"
          fi
```

`GH_REPO` is set because no checkout runs in this job — without it `gh` cannot tell which repository it is operating on.

- [ ] **Step 3: Verify the YAML parses**

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('ok')"`
Expected: `ok`

This catches indentation errors only. Steps 5–6 are the real verification.

- [ ] **Step 4: Commit the alert job before drilling it**

Commit on the working branch **first**. If you branch off with this change uncommitted, the drill branch takes it with you and the working branch loses the alert job when you switch back.

```bash
git add .github/workflows/ci.yml
git commit -m "ci: open an issue when main goes red, close it on recovery

The MTP break was loud from the first run and stayed red for six days
because nothing was listening. One issue labelled ci-red accumulates a
comment per failure rather than spawning an issue per failure, and a green
run on main closes it.

The condition is always() plus explicit needs.build.result checks, not a
bare failure(): a skipped dependency would otherwise skip the alert in
exactly the case it exists for.

Phase 2 must add the new matrix job to needs and to the result checks."
```

- [ ] **Step 5: Fire drill — prove the alert actually fires**

An alert that never fires is worse than no alert, because you believe you are covered. Verify it live on a scratch branch. Do **not** skip this step.

```bash
git checkout -b ci/alert-fire-drill
```

Add the drill branch to the push trigger in `.github/workflows/ci.yml`:

```yaml
on:
  push:
    branches: [main, ci/alert-fire-drill]
  pull_request:
    branches: [main]
```

Now break the build deliberately by raising the floor from Task 1 above the actual count:

```bash
sed -i 's|--minimum-expected-tests 130|--minimum-expected-tests 999|' tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj
git commit -am "test: deliberate failure for alert fire drill"
git push -u origin ci/alert-fire-drill
```

Watch: `gh run watch`

Expected: the `build` job fails, and the `alert` job **runs** (not skips) and reports `Opened a new ci-red issue`.

Verify: `gh issue list --label ci-red --state open`
Expected: one open issue titled "CI is red on main".

- [ ] **Step 6: Fire drill — prove recovery closes the issue**

```bash
sed -i 's|--minimum-expected-tests 999|--minimum-expected-tests 130|' tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj
git commit -am "test: restore the floor"
git push
```

Watch: `gh run watch`

Expected: `build` succeeds and the alert job reports `Closed issue #<n>`.

Verify: `gh issue list --label ci-red --state open`
Expected: no results.

- [ ] **Step 7: Tear down the drill**

```bash
git checkout ci/rig-guards
git branch -D ci/alert-fire-drill
git push origin --delete ci/alert-fire-drill
```

Confirm the drill did not leak onto the working branch:

Run: `grep -n "branches:" .github/workflows/ci.yml`
Expected: only `[main]` — no `ci/alert-fire-drill`. Shipping that trigger would make every push to that branch name run CI.

Run: `grep -n "minimum-expected-tests" tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj`
Expected: `130`, not `999`.

Run: `git status --short`
Expected: clean. The alert job is already committed from Step 4; both drill commits live only on the deleted branch.

---

### Task 4: Enable `enforce_admins`

**Files:**
- No repository files. This is a GitHub settings change.

**Interfaces:**
- Consumes: the existing branch protection on `main`, which already requires the `build` check.
- Produces: nothing later tasks depend on.

**Background for the implementer:** Branch protection already requires the `build` check, but `enforce_admins` is off — which is how six red commits reached `main` during the outage. Verified that no workflow pushes directly to `main` (release-please operates through PRs and tags), so automation is unaffected.

**Do this last**, after Tasks 1–3 have merged, so the phase's own PR does not have to fight a rule that was just switched on.

**Consequence the repo owner must accept:** they lose the ability to merge a red PR or push straight to `main`, including when they are certain it is fine. That is the point of the change, but it is a real change to how they work on their own repo.

- [ ] **Step 1: Record the current state so the change is reversible**

Run:

```bash
gh api repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins
```

Expected: `"enabled": false`

- [ ] **Step 2: Enable it**

```bash
gh api -X POST repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins
```

- [ ] **Step 3: Verify**

```bash
gh api repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins --jq '.enabled'
```

Expected: `true`

**Verification limit, stated honestly:** this confirms the setting is on. It does not prove a red merge is blocked, because proving that would mean deliberately pushing a red commit at `main` — which is precisely what the setting exists to prevent. The read-back is the appropriate level of verification here.

To revert at any time:

```bash
gh api -X DELETE repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins
```

- [ ] **Step 4: Nothing to commit**

This task changes no files. Do not create an empty commit.

---

## Done when

- `dotnet test -c Release` passes locally and in CI with the floor active.
- `dotnet --version` reports `10.0.400` in the repo root.
- `grep -n "dotnet-version" .github/workflows/*.yml` returns no matches.
- The fire drill has opened and then closed a `ci-red` issue.
- `enforce_admins` reads back `true`.
- No `ci/alert-fire-drill` branch remains, locally or on the remote.

## Handoff to Phase 2

Phase 2 introduces the `test` matrix job. It **must** update the `alert` job's `needs: [build]` to `needs: [test, build]` and add `needs.test.result == 'failure'` to the open-issue condition, plus require `needs.test.result == 'success'` for the recovery step. Leaving `needs` at `[build]` would mean a failing matrix leg skips `build` and the alert never fires.
