# Testing Rig Hardening — Design

**Date:** 2026-08-21
**Status:** Approved for planning
**Author:** Marcel Roozekrans (with Claude)

## Context

Two independent failures escaped the current testing rig within a month of each
other. They are different in kind, and fixing one does nothing for the other.

### Escape 1 — the rig stopped running, and nobody noticed

`dda705a` (#143, 2026-08-15) bumped `xunit.v3` to 4.0.0, which pulls in
Microsoft.Testing.Platform v2. MTP v2 removed the VSTest bridge and hard-errors
when invoked through the legacy VSTest target on a .NET 10 SDK. The repo had no
`global.json`, so `dotnet test` still defaulted to VSTest mode:

```
Microsoft.Testing.Platform.MSBuild.targets(320,5): error : Testing with VSTest
target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and
later.
```

CI failed on `main` for eight consecutive runs, and `main` stayed red for six
days until #150 landed on 2026-08-21. Every Renovate PR in that window failed for
the same reason rather than for its own dependency change. `release-please` also runs `dotnet test`, so releases were blocked too.
Fixed in #150 by adding `global.json` with the MTP runner opt-in.

The failure was loud from the first run. Nothing was listening.

### Escape 2 — the rig ran fine and tested nothing real

`dccd733` (#118) records the other shape:

> `ZipFile.ExtractToDirectory` discards Unix permission bits, so every file in
> the downloaded `.nupkg` lands as 0644. The installer chmodded only the entry
> point, which is not enough: `dotMemory.sh` execs `runtime-dotnet.sh`, which
> execs a native host, and the chain died on the second hop with
> `exec: .../runtime-dotnet.sh: Permission denied`.

Three of six tools — `snapshot`, `compare_snapshots`, `analyze` — were unusable
on Linux and macOS. All 137 tests were green throughout, and necessarily so.

### Why the current suite could not have caught either

Verified against the tree at `ae0e957`:

- **No test spawns a real process.** Zero hits for `Process.Start` or
  `new ProcessRunner()` under `tests/`. Everything routes through
  `FakeProcessRunner`, which never execs anything — so a missing execute bit is
  structurally invisible.
- **No test touches `Program.cs`, the DI container, or the MCP protocol.** Zero
  hits for `CreateApplicationBuilder`, `IHost`, or `ServiceProvider`. The server
  entry point, DI wiring, and tool-registration surface are entirely untested.
- **The existing `tests/.../Integration/` directory is not integration tests.**
  All three files wire real classes to `FakeProcessRunner` in-process. They are
  good component tests under a misleading name.
- **CI is `ubuntu-latest` only** across all six workflows. #118 was a
  Linux/macOS bug found in production; there is no macOS signal at all.
- **Parser tests feed hand-written gcdump text.** If JetBrains changes the real
  report format, every test stays green and the product silently stops finding
  anything.

Two further untested seams found while designing:

- `Program.cs` uses `WithToolsFromAssembly()` — reflection-based tool discovery.
  A renamed class or changed attribute drops a tool from the manifest with no
  compile error.
- `MemoryLens.Mcp.csproj` stamps `.mcp/server.json` at pack time via an inline
  Roslyn task doing a regex replace. If the regex stops matching, a package
  ships with a wrong version and nothing complains.

`samples/DotMemoryPathTest/` is a hand-run integration test — a console app
exercising the real `ProcessRunner` and `DotMemoryToolManager`, exiting 0/1. It
is not in the solution and CI never builds it. The project already wanted this
tier; it has been running it manually.

## Goals

1. Make it impossible for the test rig to stop running without someone finding
   out the same day.
2. Cover the seams where real processes, real files, real permissions, and the
   real MCP protocol are involved — the places both escapes lived.
3. Keep the PR gate fast, hermetic, and trustworthy. A nightly flake must never
   be able to block a merge.

## Non-goals

- Rewriting or reorganising the existing 137 unit tests. They are fine at what
  they do; they are simply not the tier that catches these bugs.
- Raising coverage as a number. This design targets specific, demonstrated
  failure modes.
- Testing JetBrains' profiler itself. We test that our integration with it
  works.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Tier structure | Two separate test projects | The tiers have different dependencies (network, Docker, live processes), runtimes (seconds vs minutes), and failure semantics. Physical separation makes the PR gate's hermeticity structural, not a filter someone must remember. |
| OS coverage | ubuntu + macos + windows | #118 was Unix-only and shipped. Windows currently has no coverage of any kind. |
| E2E cadence | Nightly + manual dispatch | Real profiler downloads are slow and network-dependent. Never gates a PR. |
| Shared helpers | A `TestSupport` classlib | `McpStdioClient` is a real component (process spawn, JSON-RPC framing, id correlation, timeouts, disposal), not a snippet to link into two projects. |
| SDK roll-forward | `latestPatch` | Locks the feature band where behaviour changes land; still takes bugfix patches. |

## Section 1 — The two tiers

### `tests/MemoryLens.Mcp.IntegrationTests`

Hermetic. Runs on every PR, on all three OSes. No network, no Docker, no
dotMemory. All fixtures constructed by the test itself.

| Test | Proves | Escape closed |
|---|---|---|
| Execute-bit restoration — build a zip containing a `#!` shebang script, an ELF-magic file, a Mach-O-magic file, an `MZ` managed assembly and a plain `.txt`; run real extraction + `MakeToolsExecutable`; assert modes | The byte-sniffing classifier sets exec bits on exactly the right files | #118 directly |
| Real exec chain — two-hop fixture where script A execs script B, run through the real `ProcessRunner` after extraction | The second hop actually runs | #118's real failure mode |
| Cache-hit path — run `MakeToolsExecutable` twice | The `UserExecute` early-skip does not wrongly skip a file | Regression in that optimisation |
| stdio JSON-RPC handshake — spawn the built server, `initialize`, `tools/list` | Server starts, DI resolves, reflection-discovered tool set is exactly the expected six names | Broken wiring, silently dropped tool |
| `get_rules` over the wire — real `tools/call` | A tool round-trips real JSON-RPC, not just a C# method call | Serialization/schema breakage |
| Config loading — real `.memorylens.json` in a temp cwd | `ConfigLoader` reads from the working directory as `Program.cs` assumes | Silent config no-op |

On Windows the exec-bit tests self-skip, since `MakeToolsExecutable` early-returns
via `OperatingSystem.IsWindows()`. The stdio and config tests still run there,
which is coverage that does not exist today.

### `tests/MemoryLens.Mcp.EndToEndTests`

Nightly and manually dispatchable. Never gates a PR.

| Test | Proves |
|---|---|
| Real install — real nuget.org download, real extraction, real exec | The actual JetBrains package layout still works. This is `samples/DotMemoryPathTest` promoted to an automated test |
| Live leak → snapshot → analyze | Launch the leaky fixture, take a real snapshot, run `analyze`, assert **specific rule IDs** fire. The core product promise, against real dotMemory output |
| `compare_snapshots` on two real snapshots of a growing heap | Growth is detected on real data, not hand-written fixture text |
| Docker — `docker build`, run the image, assert `tools/list` answers | The shipped image starts |
| npm shim — execute `npm/bin/memorylens-mcp.js`, assert `tools/list` answers | The shipped wrapper starts |
| Pack assertion — `dotnet pack`, unzip, assert `.mcp/server.json` version matches | The regex version stamp actually fired |

## Section 2 — CI workflow layout

### Constraint: the required check is named `build`

Branch protection requires the status check `build`, which is today's job id in
`ci.yml`. Converting `build` into a 3-OS matrix would rename the checks to
`build (ubuntu-latest)` and friends, and the required context `build` would never
report — stranding every PR on a pending check forever.

The matrix therefore goes in a new job, and `build` survives as a single
aggregating gate.

```
ci.yml  (pull_request + push to main)
├── test   [matrix: ubuntu-latest, macos-latest, windows-latest]
│   └── restore → build → unit tests + IntegrationTests
├── build  [needs: test]              ← unchanged name, still the required check
│   └── gitversion → build → pack → nuget push (main only)
└── alert  [needs: [test, build], see condition below]
    └── open-or-update "CI is red on main" issue
```

No branch-protection change is required, and `build` remains a stable gate even
if OS legs are added later.

**The alert condition must not be a bare `if: failure()`.** When `test` fails,
`build` is skipped rather than failed, and a naive dependent job can be skipped
along with it — producing an alert job that silently never fires in exactly the
case it exists for. It must depend on both jobs and inspect their results
explicitly:

```yaml
alert:
  needs: [test, build]
  if: >-
    always() && github.event_name == 'push' &&
    (needs.test.result == 'failure' || needs.build.result == 'failure')
```

The same applies to `e2e.yml`'s alert job: `always()` plus explicit
`needs.<job>.result == 'failure'` checks, never a bare `failure()`.

```
e2e.yml  (schedule: nightly + workflow_dispatch)
├── profiler  [matrix: ubuntu, macos, windows]   real dotMemory → snapshot → analyze → compare
├── docker    [ubuntu]                           docker build → run → tools/list
├── npm       [ubuntu]                           node bin shim → tools/list
├── pack      [ubuntu]                           dotnet pack → unzip → assert server.json version
└── alert     [needs: all, always() + result checks]   open-or-update "Nightly E2E failed" issue
```

### Cost

PR CI moves from roughly 30s to roughly 2–4 minutes (3× matrix plus the
integration tier). Mitigated with NuGet caching and a `concurrency` group that
cancels superseded PR runs — which matters more once every push costs 3×.
Nightly runs 10–20 minutes, dominated by the profiler download.

## Section 3 — Rig guards

These are independent of the tiers and close the hole that cost six days.

### Minimum expected test count

```xml
<TestingPlatformCommandLineArguments>--minimum-expected-tests 130</TestingPlatformCommandLineArguments>
```

Per test application, not per solution. `130` against today's 137 is a floor, so
adding tests never requires a bump, but a collapse in discovery fails loudly.
This guards the dangerous variant of Escape 1: not the loud error that actually
occurred, but a run discovering **zero** tests and reporting success. Each new
test project gets its own floor as it fills.

### SDK pin

```json
{
  "sdk": { "version": "10.0.400", "rollForward": "latestPatch" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

Paired with switching `setup-dotnet` from `dotnet-version: 10.0.x` to
`global-json-file: ./global.json`, so the runner installs exactly what the repo
pins. Renovate can bump the pin as a reviewable PR.

**Caveat, recorded deliberately:** this would *not* have caught Escape 1. That
break came from the xunit v4 bump meeting an SDK major already in use. What the
pin closes is the adjacent hole — a runner-side SDK bump changing build behaviour
with no repo change at all, which is harder to diagnose because `git log` shows
nothing.

### Red-main alert

An `alert` job gated on `if: failure() && github.event_name == 'push'`, with
`issues: write`. It searches for an open issue labelled `ci-red`, comments if one
exists, opens one if not — so six consecutive red commits produce one issue with
six comments rather than six issues. A companion step on green closes any open
`ci-red` issue with a "recovered on `<sha>`" comment.

**This is the guard that would have saved the six days.**

### `enforce_admins`

```
gh api -X POST repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins
```

Branch protection already requires the `build` check, but `enforce_admins` is off,
which is how six red commits reached `main`. Verified that no workflow pushes
directly to `main` — release-please operates through PRs and tags — so automation
is unaffected.

Consequence: the repo owner loses the ability to merge a red PR or push straight
to `main`. That is the point, but it is a real change to how the owner works on
their own repo. Reversible with `-X DELETE`.

This is a GitHub settings change, applied as an explicit separate step rather
than bundled into a code PR.

> **Current state (2026-08-22): `enforce_admins` is `false`.** It was enabled as
> planned when Phase 1 landed, then deliberately turned back off at the repo
> owner's request the same day, restoring admin override on `main`.
>
> **This guard is therefore inert for the repo owner.** The `build` check is still
> required and still binds every non-admin contributor, but the owner can merge a
> red PR or push straight to `main`. That is precisely the configuration under
> which six red commits reached `main` during the outage this document describes —
> the check was required then too, and was bypassed.
>
> The immediate trigger was practical: branch protection also requires one
> approving review, and in a single-maintainer repo the owner cannot approve their
> own PR, so every PR was blocked. Worth noting that dropping
> `required_approving_review_count` to `0` while keeping `enforce_admins` on would
> have removed that friction without making the CI gate advisory — if the review
> requirement is the real obstacle, that remains the narrower fix.
>
> Re-enable with:
> ```
> gh api -X POST repos/MarcelRoozekrans/memorylens-mcp/branches/main/protection/enforce_admins
> ```
> Tracked in issue #156.

## Section 4 — Fixture and shared test support

### `tests/MemoryLens.Mcp.LeakyApp`

A console app that leaks on purpose, in shapes the rules detect: a static event
with accumulating subscribers (ML001), a growing static collection (ML002),
undisposed streams (ML003), retained closure display-classes (ML007), duplicated
strings (ML010).

`compare_snapshots` needs two snapshots of the *same* process with real growth
between them, so the app needs a control channel. Stdin commands, chosen over
ports or sentinel files because there is no binding, no polling, and no timing
race:

```
stdout: READY <pid>
stdin:  grow  → allocate and retain another tranche → stdout: GROWN
stdin:  exit  → clean shutdown
```

The E2E test waits for `READY`, snapshots, sends `grow`, waits for `GROWN`, and
snapshots again.

This lets the `analyze` test assert **specific rule IDs fired** rather than
`count > 0` — and it is the only thing in this design that can catch a JetBrains
report-format change, since every current parser test feeds hand-written text.

Lives under `tests/` rather than `samples/` so CI compiles it and a broken fixture
surfaces as a build error. Excluded from packing.

### `tests/MemoryLens.Mcp.TestSupport`

A classlib referenced by both tiers, holding:

- **`McpStdioClient`** — spawns a child process, frames JSON-RPC over stdio,
  correlates request/response ids, enforces timeouts, kills the process on
  dispose. The integration tier points it at the built DLL; the E2E tier points
  the same client at the Docker image and the npm shim. One client, three
  transports' worth of coverage.
- **`TempDir`** — disposable temp directory.
- **`PackageFixtureBuilder`** — constructs a zip with shebang / ELF / Mach-O /
  `MZ` / plain-text entries for the exec-bit tests.

### Flakiness discipline

The fastest way to waste this investment is a nightly that cries wolf.

- Every spawned process gets a hard timeout and is killed on dispose.
- Every test gets its own temp directory; never a shared path.
- Nothing depends on wall-clock ordering or `Thread.Sleep`.
- Profiler tests get generous attach timeouts; dotMemory is genuinely slow to
  attach.

### Wiring

All four new projects go into `memorylens-mcp.slnx`. `InternalsVisibleTo` gains
`MemoryLens.Mcp.IntegrationTests` and `MemoryLens.Mcp.EndToEndTests` — the
exec-bit tests need `MakeToolsExecutable`, which is `internal static`.
`TestSupport` and `LeakyApp` do not need it.

## Sequencing

Three phases, each independently shippable. The guards come first because they
are small, independent, and close the hole that actually cost six days — there is
no reason to hold them behind the larger build.

**Phase 1 — Rig guards.** Min-test-count, SDK pin plus `global-json-file`,
red-main alert, `enforce_admins`. Small, no new projects. **Shipped** as #153 plus
the settings change; note that `enforce_admins` was subsequently turned back off —
see the current-state note in Section 3.

**Phase 2 — Integration tier.** `TestSupport`, `IntegrationTests`, the `test`
matrix job and the `build` aggregating gate. This is where #118's regression
guard lands.

**Phase 3 — E2E tier.** `LeakyApp`, `EndToEndTests`, `e2e.yml`. Gated on the
licence question below.

### Phase 2 prerequisites discovered during Phase 1

Phase 1 shipped as `a89a234..f0776d9`. Executing it surfaced four traps that
Phase 2 must handle. They are recorded here rather than in an execution ledger
because that ledger is scratch and will not survive.

1. **The alert's `needs` list is load-bearing.** Phase 1's alert job is
   `needs: [build]` and its open-issue step fires on `needs.build.result != 'success'`
   — deliberately `!= 'success'` rather than `== 'failure'`, because a fail-fast
   matrix *cancels* sibling legs and `cancelled` would otherwise alert nobody.
   When Phase 2 introduces the `test` matrix job, it must add `test` to `needs`
   **and** to the result checks. Adding it to `needs` alone reintroduces the gap.

2. **The min-test floor must move with the tests.** The floor of `130` is
   calibrated against 137 in `MemoryLens.Mcp.Tests`. If the 22 tests under
   `tests/MemoryLens.Mcp.Tests/Integration/` are relocated into the real
   integration tier — a natural move, since this spec calls that directory
   misleadingly named — the count drops to roughly 115 and the floor fails
   as what looks like a false alarm. Lower the floor in the same commit that
   moves the tests.

3. **Windows self-skips must not break the new project's floor.** Section 1
   says the exec-bit tests self-skip on Windows. If they use xunit's `Skip =`,
   they may not count toward `--minimum-expected-tests`, so a floor calibrated
   on Linux fails the Windows leg. The existing suite avoids this by branching
   *inside* the test body (see `DiagnosticPortProcessListerTests.cs` and
   `DotMemoryAutoInstallerTests.cs`), which keeps them counted as passed.
   Follow that pattern, or make the new project's floor OS-conditional.

4. **The Docker build is outside the SDK pin, deliberately.** `Dockerfile` builds
   on the floating `mcr.microsoft.com/dotnet/sdk:10.0` tag and does **not** copy
   `global.json`. Copying it would hard-break the image as soon as that tag rolls
   to a `10.0.5xx` band, since the pin is `latestPatch`. A comment in the
   Dockerfile records this. Phase 3's Docker E2E test must not "fix" it.

## Risks and open questions

**dotMemory licence for CI use — resolve before Phase 3.** The nightly tier
downloads and runs `JetBrains.dotMemory.Console` on GitHub-hosted runners. Whether
JetBrains' licence permits automated CI use has not been verified, and this design
does not assume it does.

Resolution required before the `profiler` job is enabled. If CI use turns out to
be restricted, the fallback is defined: the `profiler` leg becomes self-hosted or
manual-dispatch-only, and the `docker`, `npm`, and `pack` legs stay nightly.
Phases 1 and 2 are unaffected either way.

**PR latency.** 3× matrix plus a new tier is a real cost on a repo whose CI
currently finishes in 30 seconds. Caching and run-cancellation are part of Phase 2
rather than a later optimisation.

**Nightly trust.** If the nightly goes flaky it will be ignored, and an ignored
alarm is worse than none. The flakiness discipline above is a requirement, not a
guideline. If a nightly test cannot be made deterministic, it should be deleted
rather than tolerated.

## Appendix — Verified repro

In a clean worktree at `ae0e957`, SDK 10.0.400:

| State | Result |
|---|---|
| `main` as merged | `Passed! total: 137, failed: 0` |
| `rm global.json` | `MTP targets(320,5): error : Testing with VSTest target is no longer supported` — build fails, 0 tests run |
| `git checkout global.json` | `Passed! total: 137` |

The failure is a pure function of that one file.
