# AGENTS.md: memorylens-mcp

MemoryLens MCP is an MCP server that profiles the heap of a running .NET process in-process over
EventPipe and returns rule-based findings with code fix suggestions. It ships as the `MemoryLens.Mcp`
.NET global tool on NuGet, as the `memorylens-mcp` npm launcher, as a Docker image, and as a Claude
Code plugin that runs the tool through `dnx`.

## Layout

- `src/MemoryLens.Mcp` is the only packable project, on `net10.0` and `net11.0`.
- `tests/MemoryLens.Mcp.Tests` is the unit suite. `tests/MemoryLens.Mcp.IntegrationTests` starts the
  server over stdio and attaches to `tests/MemoryLens.Mcp.LeakyApp`, a child process it copies into
  its output folder. `LeakyApp` targets only `net10.0` with `RollForward` set to `Major`, so it runs
  under both test target frameworks. `tests/MemoryLens.Mcp.TestSupport` is shared helper code.
- `npm/` is a launcher with no server code that installs the NuGet tool at its own version, so it is
  published after the NuGet push. Its test, `cd npm && node --test test/*.test.js`, runs in no CI job.
- `src/MemoryLens.Mcp/.mcp/server.json` is a committed `0.0.0` placeholder. The `StampMcpServerJsonVersion`
  target in the csproj writes the real `PackageVersion` into a copy under `obj/` and packs that copy.
  Never edit the version in the source file and never make the target write back over it.
- A version is bumped only by release-please. The files it rewrites are declared in
  `release-please-config.json` `extra-files`: root `server.json` (three fields), `npm/package.json`,
  `.claude-plugin/marketplace.json` (two fields) and `plugins/memorylens/.claude-plugin/plugin.json`
  (the version and the `dnx` version argument). Adding another file that carries the version means
  adding it there too. Do not edit those versions by hand.
- `Dockerfile` publishes `net11.0` only and deliberately does not copy `global.json`: the floating
  `sdk:11.0` tag would hard-break against the pin once it rolls past the 11.0.1xx band.

## Build and test

Prerequisites: the .NET 11 SDK (`global.json` pins 11.0.100 with `rollForward: latestPatch`, so SDK 10
cannot build the `net11.0` target) and the .NET 10 runtime, because the unit and integration test
projects run on both `net10.0` and `net11.0`. The npm test needs Node.

CI job `test` in `.github/workflows/ci.yml`, on the ubuntu, macos and windows legs:

```bash
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --coverage --coverage-output-format cobertura --coverage-settings coverage.config.xml --results-directory TestResults
```

The Linux leg then runs the external `ci-baseline` coverage gate on `TestResults/*.cobertura.xml`;
the commands above do not reproduce it.

CI job `build` in `ci.yml` packs with a GitVersion version and asserts the stamp:

```bash
dotnet tool restore
VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
rm -rf artifacts
dotnet pack src/MemoryLens.Mcp/MemoryLens.Mcp.csproj --no-build -c Release -p:PackageVersion=$VERSION -o ./artifacts
unzip -o -q "artifacts/MemoryLens.Mcp.$VERSION.nupkg" -d artifacts/extracted
jq -e --arg v "$VERSION" '[.version, .packages[].version] | all(. == $v)' artifacts/extracted/.mcp/server.json
```

CI job `pack` in `.github/workflows/e2e.yml` (nightly) asserts the same stamp with a fixed version:

```bash
rm -rf artifacts
dotnet pack src/MemoryLens.Mcp/MemoryLens.Mcp.csproj -c Release -p:PackageVersion=9.9.9 -o ./artifacts
unzip -o -q artifacts/MemoryLens.Mcp.9.9.9.nupkg -d artifacts/extracted
jq -e --arg v 9.9.9 '[.version, .packages[].version] | all(. == $v)' artifacts/extracted/.mcp/server.json
```

CI jobs `docker` and `npm` in `e2e.yml` are nightly smoke tests that list the five tools. The
`docker` job builds the image from local source with `docker build -t memorylens-mcp:e2e .`; the `npm`
job runs the launcher against the published NuGet tool, so it proves nothing about local source.

Inner loop, not run by a CI job:

```bash
dotnet test -c Release --project tests/MemoryLens.Mcp.Tests -f net11.0
dotnet test -c Release --project tests/MemoryLens.Mcp.Tests -f net11.0 --filter-class "*ML001Tests"
```

- A filtered run exits with code 9 although every test passed. Each test project sets
  `--minimum-expected-tests` through `TestingPlatformCommandLineArguments`, and a filter leaves fewer
  tests than that floor. Check that `total:` is above 0 and `failed:` is 0; exit code 0 or 9 is then a pass, any other code a real failure. A full run is unaffected.
  Recalibrate a floor to the real count minus 2 when tests change: 102 unit, 16 integration.
- The integration tests start real server processes, and `Host.CreateApplicationBuilder` watches its
  config files with one inotify instance each. On a machine near its inotify limit (Linux default 128)
  they fail at server start with an `IOException`. Run them with
  `DOTNET_hostBuilder__reloadConfigOnChange=false` set, which the child processes inherit.

## Conventions and gotchas

- Heap collection is in-process over EventPipe. Nothing is downloaded, shelled out to or required on
  `PATH` at runtime. Do not reintroduce an external profiler; the dotMemory installer was removed on
  purpose and both nightly smoke tests assert that `ensure_dotmemory` stays absent.
- The server exposes exactly five tools: `list_processes`, `snapshot`, `compare_snapshots`, `analyze`
  and `get_rules`. Both nightly smoke tests check them by name, so a rename or a new tool needs those
  checks updated in the same change.
- `.memorylens.json` is read from the working directory the server starts in, so the npm launcher
  starts the server in the caller's directory and runs only its SDK checks from the home directory.
- `Directory.Build.props` adds the analyzers to every project and turns on NuGetAudit at level
  `moderate`. Only the test project has an `.editorconfig` that switches off Meziantou rules.
- NuGet publishing happens only in the `publish` job of `release-please.yml`; `ci.yml` never pushes.
- `GitVersion.yml` must use the v6 key `deployment-mode`; the old `mode:` is silently ignored.

<!-- org-core:start sha256=08a88996bc9165f888fc0260607cfd0670cae128c323e16707c6b452ee66222d (generated from docker-nuc scripts/agents-md/org-core-personal.md; do not edit here) -->
## Org rules

These rules hold in every public MarcelRoozekrans repo.

### Commits and releases

- Conventional Commits. release-please reads them on the default branch, and the changelog sections
  in `release-please-config.json` decide which types count. The standard: `feat`, `fix`, `perf`,
  `deps` and `revert` open or update the release PR; `docs`, `test`, `refactor`, `build`, `ci`,
  `chore` and `style` do not, unless the commit is breaking or has a `Release-As:` footer. The
  release happens when that PR merges. A breaking change needs `!` or a `BREAKING CHANGE:` footer.
- A commit body never contains nested parentheses. release-please cannot parse them and silently
  drops the whole commit from the changelog, while the workflow still reports success.
- After merging a commit that should release, check that it appears in the release-please PR.

### Branches and pull requests

- Every change goes through a branch and a pull request. Never push to the default branch directly.
- A PR body says what the diff does, and nothing the diff does not do.
- Fix review findings in the PR under review, including small ones. File an issue only for a finding
  that belongs to other code, needs a design decision, is its own piece of work, or waits on
  something external.

### CI

- The .NET repos call the shared workflows of `MarcelRoozekrans/ci-baseline`, pinned to `@v1`, never
  `@main`.
- Its coverage gate fails the build when line coverage drops below the floor of 80%, and Codecov
  fails a PR that lowers project coverage by more than 1 percentage point. New code comes with
  tests.
- A repo with a lower floor documents the reason and links the issue that raises it.
<!-- org-core:end -->
