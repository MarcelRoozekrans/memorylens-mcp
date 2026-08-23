# Part 2 Implementation Plan — Identity, Generations, and the Nightly Tier

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make 2.0.0 releasable — the product stops describing itself as a dotMemory wrapper, the Docker image sheds the SDK it no longer needs, ML005 becomes able to fire, and the nightly tier proves the shipped artifacts start.

**Architecture:** Three independent strands. Identity is text: ten files still say the product wraps JetBrains dotMemory, which stopped being true in Part 1. Generations are a small addition to `HeapCollector` — one extra EventPipe keyword publishes generation address ranges, and bucketing node addresses into them populates `TypeInfo.DominantGeneration`, which unblocks ML005 and makes LOH classification exact rather than heuristic. The nightly tier is a new workflow that runs the Docker image and npm shim through the existing `McpStdioClient`.

**Tech Stack:** .NET 10 (`net10.0`), `Microsoft.Diagnostics.NETCore.Client`, `Microsoft.Diagnostics.Tracing.TraceEvent`, xunit.v3, Microsoft.Testing.Platform, GitHub Actions, Docker.

**Spec:** `docs/superpowers/specs/2026-08-22-in-process-heap-collection-design.md` — see "Section 3 — Identity", "Section 4 — Testing / Nightly tier", and the "Handoff to Part 2" section of `docs/superpowers/plans/2026-08-22-in-process-heap-collection-part-1.md`.

## Global Constraints

- Target framework is `net10.0`. Do not change it.
- `global.json` keeps its SDK pin (`10.0.400` / `latestPatch`) and `"test": {"runner": "Microsoft.Testing.Platform"}`. Do not edit it.
- The required branch-protection status check is named exactly **`build`**. Do not rename `build` or the `test` matrix job in `.github/workflows/ci.yml`.
- **OS-conditional tests branch INSIDE the body with an early `return`, never xunit's `Skip =`.** A skipped test may not count toward `--minimum-expected-tests`, failing the Windows leg against a floor calibrated on Linux.
- **`HeapCollector` must never return an empty or partial `SnapshotData`.** Collecting nothing, or timing out, throws. This is the lesson of #161 and it survived two review rounds — do not weaken it while adding generations.
- Every spawned process gets a hard timeout and is killed on dispose. No `Thread.Sleep` in tests.
- Each test project's `--minimum-expected-tests` floor is recalibrated from a real run to actual-minus-2, and verified to fire in the failing direction.
- Conventional Commits. Part 1 already declared the `feat!:` breaking change; do not declare another.

## Verified Facts (measured during a spike — do not re-derive)

1. **Generation ranges need one extra keyword.** With only `ClrTraceEventParser.Keywords.GCHeapSnapshot`, `GCGenerationRange` fires **0 times** while 25,485 nodes arrive carrying addresses. Adding `ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement` produces **10** `GCGenerationRange` events covering generations 0–4.

2. **Measured distribution** on the `LeakyApp` fixture with both keywords, 28,905 nodes:
   ```
   gen 0: 2,899   gen 1: 25,336   gen 3: 17   gen 4: 1   unmapped: 652
   ```
   **Generation 2 received zero objects.** See Task 4's warning — this is why the generation test asserts the plumbing, not ML005 firing.

3. **The event shapes** (confirmed by reflection against TraceEvent 3.1.16):
   - `GCBulkNodeValues` exposes `ulong Address`, `ulong Size`, `ulong TypeID`, `long EdgeCount`.
   - `GCGenerationRangeTraceData` exposes `int Generation`, `ulong RangeStart`, `ulong RangeUsedLength`, `ulong RangeReservedLength`.
   - Bucket an object by `Address >= RangeStart && Address < RangeStart + RangeUsedLength`.

4. **Generation 3 is the Large Object Heap and generation 4 is the Pinned Object Heap.** `IsLargeObjectHeap` is currently the heuristic `avg >= 85_000`; generation data makes it exact.

5. **Roughly 2.3% of nodes map to no range** (652 of 28,905). Leaving those at `DominantGeneration = -1` matches the existing default and is the correct policy — do not guess a generation for them.

6. **The Docker runtime stage needs the SDK for no reason.** `Dockerfile:41-44` says it must be the SDK image because `DotMemoryToolManager` shells `dotnet tool install -g`. That class was deleted in Part 1. The only `dotnet restore` is in the build stage (`Dockerfile:32`); nothing at runtime needs the SDK.

7. **Ten files still claim dotMemory** (hit counts from `grep -ci dotmemory`): `README.md` 22, `docs/docker.md` 8, `Dockerfile` 6, `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj` 2, `.claude-plugin/plugin.json` 2, `.claude-plugin/marketplace.json` 2, `npm/package.json` 2, `npm/README.md` 2, `server.json` 1, `src/MemoryLens.Mcp/.mcp/server.json` 1.

8. **The product has five tools**: `snapshot`, `compare_snapshots`, `analyze`, `list_processes`, `get_rules`.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/MemoryLens.Mcp/Profiler/HeapCollector.cs` | Modify | Add the keyword, collect ranges, bucket addresses, populate `DominantGeneration`, derive `IsLargeObjectHeap` from generation |
| `src/MemoryLens.Mcp/Rules/BuiltIn/ML005_ObjectRetainedTooLong.cs` | Modify | Remove the "cannot fire" remark added in Part 1 |
| `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs` | Modify | Generation and exact-LOH coverage |
| `README.md` | Modify | Delete the dotMemory installation section; rewrite Prerequisites, the tool table and both usage examples |
| `server.json`, `src/MemoryLens.Mcp/.mcp/server.json` | Modify | Description |
| `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj` | Modify | `<Description>`, `<PackageTags>` |
| `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json` | Modify | Description, keywords |
| `npm/package.json`, `npm/README.md` | Modify | Description, keywords |
| `Dockerfile` | Modify | Runtime stage to `dotnet/runtime:10.0`; rewrite the stale comments |
| `docs/docker.md` | Modify | Remove the profiler-download volume guidance |
| `.github/workflows/e2e.yml` | Create | Nightly Docker / npm / pack verification |

## Before you start

```bash
git fetch origin
git checkout -b feat/part-2-identity-and-generations origin/main
```

"The working branch" below means `feat/part-2-identity-and-generations`.

**A note on ordering.** Tasks 1–3 (identity, Docker, docs) are what block a releasable 2.0.0 and are pure text plus a base-image change. Tasks 4–5 (generations) are a behaviour change. Task 6 is CI. They are independent — if you need to ship earlier, Tasks 1–3 stand alone.

---

### Task 1: Package and plugin identity

**Files:**
- Modify: `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj:15,19`
- Modify: `server.json`
- Modify: `src/MemoryLens.Mcp/.mcp/server.json`
- Modify: `.claude-plugin/plugin.json`
- Modify: `.claude-plugin/marketplace.json`
- Modify: `npm/package.json`
- Modify: `src/MemoryLens.Mcp/Tools/SnapshotTool.cs:16-17`
- Modify: `src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs:17-18`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing later tasks depend on. Task 2 handles `npm/README.md` prose alongside the other docs.

**Background:** Every string below ships to a user — on nuget.org, npmjs.com, in the MCP registry, and in the plugin marketplace. They all say the product wraps JetBrains dotMemory. Since Part 1 it collects heap data in-process over EventPipe with nothing to install.

**The replacement description**, used consistently everywhere a one-liner is needed:

> MCP server for .NET memory profiling — collects heap snapshots in-process via EventPipe and applies a heuristic rule engine

- [ ] **Step 1: csproj**

In `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`, replace line 15:

```xml
    <Description>MCP server for .NET memory profiling — wraps JetBrains dotnet-dotmemory with heuristic-based analysis rules</Description>
```

with:

```xml
    <Description>MCP server for .NET memory profiling — collects heap snapshots in-process via EventPipe and applies a heuristic rule engine</Description>
```

and line 19:

```xml
    <PackageTags>mcp;memory;profiling;dotmemory;diagnostics;dotnet-tool</PackageTags>
```

with (drop `dotmemory`, add `eventpipe`):

```xml
    <PackageTags>mcp;memory;profiling;eventpipe;diagnostics;dotnet-tool</PackageTags>
```

- [ ] **Step 2: both server.json files**

In `server.json`, replace:

```json
  "description": "MCP server for .NET memory profiling with JetBrains dotMemory integration",
```

In `src/MemoryLens.Mcp/.mcp/server.json`, replace:

```json
  "description": "MCP server for .NET memory profiling — wraps JetBrains dotnet-dotmemory with heuristic-based analysis rules",
```

Both become:

```json
  "description": "MCP server for .NET memory profiling — collects heap snapshots in-process via EventPipe and applies a heuristic rule engine",
```

**Do not touch the `version` fields in either file.** release-please stamps them (`release-please-config.json` lists both as `extra-files`), and hand-editing them will corrupt the next release.

- [ ] **Step 3: plugin.json and marketplace.json**

In `.claude-plugin/plugin.json`, replace the description:

```json
 "description": "On-demand .NET memory profiling with concrete code fix suggestions — powered by JetBrains dotMemory",
```

with:

```json
 "description": "On-demand .NET memory profiling with concrete code fix suggestions — no profiler to install",
```

and in its `keywords` array replace `"dotmemory"` with `"eventpipe"`.

In `.claude-plugin/marketplace.json`, make the same description replacement (the string is identical) and the same keyword swap.

- [ ] **Step 4: npm/package.json**

Replace the description:

```json
  "description": "MCP server for .NET memory profiling with AI-actionable code fix suggestions — wraps JetBrains dotMemory with a heuristic-based rule engine.",
```

with:

```json
  "description": "MCP server for .NET memory profiling with AI-actionable code fix suggestions — collects heap snapshots in-process via EventPipe.",
```

and in `keywords` replace `"dotmemory"` with `"eventpipe"`.

**Do not touch `version`** — release-please stamps it.

- [ ] **Step 5: Stop the tool parameters advertising what they do not do**

These strings are what the MCP client shows the calling model, so they are the
contract an agent reads. In BOTH `src/MemoryLens.Mcp/Tools/SnapshotTool.cs` and
`src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs`, two parameter descriptions
promise behaviour that does not exist — `command` is accepted but never launches
anything, and `processName` only feeds the exclusion filter:

```csharp
        [Description("Process name to snapshot")] string? processName = null,
        [Description("Command to launch and snapshot")] string? command = null,
```

Replace both, in both files, with:

```csharp
        [Description("Process name, used only to apply the profiling exclusion list")] string? processName = null,
        [Description("Not implemented; a process id is required")] string? command = null,
```

Keep the parameters themselves — removing them would break existing callers, and
a later part may implement launch-then-attach. Only the descriptions change.

- [ ] **Step 6: Verify no identity file still claims dotMemory**

Run:

```bash
grep -ci dotmemory server.json src/MemoryLens.Mcp/.mcp/server.json src/MemoryLens.Mcp/MemoryLens.Mcp.csproj .claude-plugin/plugin.json .claude-plugin/marketplace.json npm/package.json
```

Expected: `0` for every file.

- [ ] **Step 7: Verify the JSON is still valid and the build still works**

```bash
for f in server.json src/MemoryLens.Mcp/.mcp/server.json .claude-plugin/plugin.json .claude-plugin/marketplace.json npm/package.json; do python -c "import json,sys; json.load(open(sys.argv[1])); print('ok', sys.argv[1])" "$f"; done
dotnet build -c Release
```

Expected: `ok` five times, then `Build succeeded`.

- [ ] **Step 8: Commit**

```bash
git add src/MemoryLens.Mcp/MemoryLens.Mcp.csproj src/MemoryLens.Mcp/Tools server.json src/MemoryLens.Mcp/.mcp/server.json .claude-plugin npm/package.json
git commit -m "docs: stop describing the product as a dotMemory wrapper

Six shipped metadata files still said the product wraps JetBrains dotMemory.
Since the in-process rewrite it collects heap snapshots over EventPipe with
nothing to install, so every one of these strings was advertising a dependency
that no longer exists -- on nuget.org, npmjs.com, the MCP registry and the
plugin marketplace.

Also corrects two parameter descriptions that promised behaviour the tools do
not have: command never launches anything, and processName only feeds the
exclusion filter. Those strings are what the calling model reads.

Version fields deliberately untouched; release-please stamps those."
```

---

### Task 2: README and npm README

**Files:**
- Modify: `README.md`
- Modify: `npm/README.md`

**Interfaces:**
- Consumes: the replacement description wording from Task 1.
- Produces: nothing later tasks depend on.

**Background:** The README is the repo's front door and currently documents a tool that no longer exists, under a Prerequisites section listing a CLI that is never used. `grep -ci dotmemory README.md` returns 22.

- [ ] **Step 1: Fix Prerequisites**

In `README.md`, the `## Prerequisites` section currently reads:

```markdown
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), 10.0.4xx feature band (pinned in `global.json`)
- JetBrains dotMemory CLI (see below for installation options)
```

Delete the second bullet entirely. The .NET SDK bullet stays, as does the paragraph below it about the filtered-test exit code.

- [ ] **Step 2: Delete the entire dotMemory CLI Installation section**

Delete `## dotMemory CLI Installation` and every subsection under it, up to but not including `## Available MCP Tools`. That is these headings and all their content:

```
## dotMemory CLI Installation
### Supported Platforms (auto-download)
### Cache Location
### Unsupported Platforms
### Manual Fallback Discovery
### Error Scenarios
```

All of it describes machinery deleted in Part 1.

- [ ] **Step 3: Replace it with a short section on how collection works**

Where that section was, insert:

```markdown
## How Collection Works

MemoryLens collects heap data **in-process** over [EventPipe](https://learn.microsoft.com/dotnet/core/diagnostics/eventpipe), the .NET runtime's built-in diagnostics channel. There is no profiler to install, no download on first use, and no external tool on `PATH`.

`snapshot` attaches to a running .NET process by pid, induces a collection, and aggregates the heap into per-type counts and sizes. Snapshots are written as small JSON files under your temp directory and referenced by a short id.

On Linux and in containers, attaching to another process's diagnostic endpoint may require matching UID or `SYS_PTRACE` — see [docs/docker.md](docs/docker.md).
```

- [ ] **Step 4: Fix the tool table**

The `## Available MCP Tools` table currently has six rows. Delete the `ensure_dotmemory` row entirely, and replace the `list_processes` row — which references it — so the table reads:

```markdown
| Tool | Description |
|------|-------------|
| `list_processes` | Lists running .NET processes available for profiling, discovered from their diagnostic IPC endpoints |
| `snapshot` | Captures a single memory snapshot of a target process |
| `compare_snapshots` | Captures two snapshots with configurable delay and compares them |
| `analyze` | Runs the rule engine against a captured snapshot and returns findings |
| `get_rules` | Lists all available analysis rules with their metadata |
```

- [ ] **Step 5: Fix both usage examples**

Under `### Single Snapshot`, replace:

```markdown
Claude will call `ensure_dotmemory`, then `snapshot` with the target PID, then `analyze` the result and present findings ordered by severity.
```

with:

```markdown
Claude will call `snapshot` with the target PID, then `analyze` the returned snapshot id and present findings ordered by severity.
```

Read the `### Before/After Comparison` example too and correct anything that no longer matches — in particular, `compare_snapshots` takes `delaySeconds` (seconds, default 10), not an unnamed "wait period".

- [ ] **Step 6: npm/README.md**

Read it, and apply the same two corrections: the description line, and any mention of dotMemory or `ensure_dotmemory`. It is short; report what you changed.

- [ ] **Step 7: Verify**

```bash
grep -ni dotmemory README.md npm/README.md
```

Expected: no matches. If any remain, they are either a link to this project's own history or something you missed — report which.

- [ ] **Step 8: Commit**

```bash
git add README.md npm/README.md
git commit -m "docs: rewrite the READMEs for in-process collection

The front page documented a tool that no longer exists, under a Prerequisites
section listing a CLI that is never used, plus a whole section on downloading
and caching a profiler that Part 1 deleted.

Replaced with a short account of how collection actually works now, a five-tool
table, and usage examples that match the real call sequence."
```

---

### Task 3: Docker

**Files:**
- Modify: `Dockerfile`
- Modify: `docs/docker.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an image Task 6's nightly workflow builds and runs.

**Background:** `Dockerfile:41-44` states the runtime stage must be the SDK image because `DotMemoryToolManager` shells `dotnet tool install -g dotnet-dotmemory`. That class was deleted in Part 1. Nothing at runtime needs the SDK — the only `dotnet restore` is in the build stage.

- [ ] **Step 1: Read the whole Dockerfile first**

Run: `cat Dockerfile`

Note the build stage, the runtime stage, the `ENV` block, the `COPY --from=build`, the `WORKDIR`, and the `ENTRYPOINT`. You are changing the runtime base image and the comments — not the layout.

- [ ] **Step 2: Change the runtime base image**

Replace the runtime stage's comment and `FROM`:

```dockerfile
# Must be the SDK image, not dotnet/runtime: DotMemoryToolManager falls back to
# `dotnet tool install -g dotnet-dotmemory`, which requires the SDK. dotMemory
# Console is glibc-linked, so this is also why the image is not Alpine-based.
FROM mcr.microsoft.com/dotnet/sdk:10.0
```

with:

```dockerfile
# The runtime image suffices: heap collection is in-process over EventPipe, so
# nothing is installed or shelled out to at runtime. This was previously the SDK
# image only because the deleted dotMemory installer needed `dotnet tool install`.
FROM mcr.microsoft.com/dotnet/runtime:10.0
```

Leave the build stage on the SDK image — it still compiles.

- [ ] **Step 3: Fix the remaining Dockerfile comments**

`grep -n -i dotmemory Dockerfile` will show what survives, including the header block explaining that the profiler is downloaded at runtime and that a volume should be mounted for it. None of that happens now. Rewrite those comments to describe the actual image: self-contained, nothing downloaded at runtime.

The `PATH="/root/.dotnet/tools:${PATH}"` entry in `ENV` existed for the globally-installed profiler tool. Remove it if nothing else needs it; keep the other `DOTNET_*` variables.

- [ ] **Step 4: Verify the image builds and the server actually starts**

```bash
docker build -t memorylens-mcp:part2 .
```

Expected: builds successfully.

Then confirm the server starts and answers the MCP handshake — a smaller base image that cannot run the app is worse than a large one that can:

```bash
printf '%s\n%s\n%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"probe","version":"1"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  | docker run -i --rm memorylens-mcp:part2
```

Expected: an `initialize` result naming `MemoryLens.Mcp`, then a `tools/list` result containing exactly the five tools. **If it does not start, report BLOCKED with the error** rather than reverting to the SDK image on your own initiative — the runtime image is the point of this task.

Record the size difference: `docker images memorylens-mcp --format '{{.Tag}} {{.Size}}'`.

- [ ] **Step 5: docs/docker.md**

`grep -n -i dotmemory docs/docker.md` shows 8 hits. Read the file. Remove the guidance about mounting a volume so a runtime profiler download survives `--rm`, and any "call `ensure_dotmemory` first" instruction. Keep everything about `--pid=host` and `--cap-add=SYS_PTRACE`, which is still required and still correct — attaching to another process's diagnostic endpoint needs it.

- [ ] **Step 6: Commit**

```bash
git add Dockerfile docs/docker.md
git commit -m "build: drop the Docker runtime stage to dotnet/runtime

The runtime stage was the SDK image for exactly one reason: the dotMemory
installer shelled out to `dotnet tool install -g`. That installer was deleted
in the in-process rewrite, and nothing at runtime needs the SDK any more --
the only restore is in the build stage.

Also removes the docs telling users to mount a volume so a runtime profiler
download survives --rm. There is no runtime download; the image is
self-contained."
```

---

### Task 4: Populate `DominantGeneration`

**Files:**
- Modify: `src/MemoryLens.Mcp/Profiler/HeapCollector.cs`
- Modify: `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs`

**Interfaces:**
- Consumes: the existing `HeapCollector` pipeline.
- Produces: `TypeInfo.DominantGeneration` populated with a real generation (or `-1` when unmappable). Task 5 consumes it for exact LOH classification and ML005.

**Background and warnings — read before coding.**

`HeapCollector` is the most reviewed file in the repo. It closed a Critical data race and a latent self-deadlock. Two rules are load-bearing and must survive this change:

1. **The aggregation dictionaries are only ever read after the pump has provably finished.** The drain-expired path throws *before* reading them. Any new collection you add follows the same rule — write it on the pump thread, read it only where the existing code reads the others.
2. **The collector never returns an empty or partial snapshot.** Adding generations must not introduce a path that returns data when the collection did not complete.

A spike established the mechanism; do not re-derive it:
- Add `ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement` to the existing `GCHeapSnapshot` keyword. Without it, `GCGenerationRange` fires **zero** times.
- Subscribe to `source.Clr.GCGenerationRange`, whose `GCGenerationRangeTraceData` exposes `int Generation`, `ulong RangeStart`, `ulong RangeUsedLength`.
- `GCBulkNodeValues` already exposes `ulong Address` — the collector currently ignores it.
- Bucket: an object is in a range when `Address >= RangeStart && Address < RangeStart + RangeUsedLength`.
- **Roughly 2.3% of nodes map to no range.** Leave those at `-1`. Do not guess.

- [ ] **Step 1: Write the failing test**

Add to `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs`:

```csharp
    [Fact(Timeout = 120_000)]
    public async Task CollectAsync_PopulatesDominantGeneration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var pid = int.Parse((await stdout.ReadLineAsync(ct))!["READY ".Length..]);

            var data = await new HeapCollector(TestTimeout).CollectAsync(pid, ct);

            // Before generation tracking every type kept the -1 default. At least
            // some of a real heap must now carry a real generation.
            var withGeneration = data.Types.Where(t => t.DominantGeneration >= 0).ToList();
            Assert.True(withGeneration.Count > 0,
                "no type carried a generation; GCGenerationRange events are probably not being received");

            // Generations are 0..4 (3 = LOH, 4 = POH). Anything else means the
            // address bucketing is wrong, not merely incomplete.
            Assert.All(data.Types, t =>
                Assert.True(t.DominantGeneration >= -1 && t.DominantGeneration <= 4,
                    $"{t.FullName} reported generation {t.DominantGeneration}"));

            // The bulk of a live heap should map. If most types are -1 the ranges
            // arrived but the bucketing is not matching addresses.
            Assert.True(withGeneration.Count > data.Types.Count / 2,
                $"only {withGeneration.Count} of {data.Types.Count} types mapped to a generation");
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release --filter CollectAsync_PopulatesDominantGeneration`

Expected: FAIL on the first assertion — every type still has `-1`.

- [ ] **Step 3: Add the keyword**

In `HeapCollector.CollectAsync`, the runtime provider currently requests `(long)ClrTraceEventParser.Keywords.GCHeapSnapshot`. Change it to:

```csharp
                (long)(ClrTraceEventParser.Keywords.GCHeapSnapshot
                     | ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement),
```

Leave the `Microsoft-DotNETCore-SampleProfiler` provider and the 1024 MB buffer exactly as they are — both are load-bearing and were established by earlier spikes.

- [ ] **Step 4: Collect the ranges and the addresses**

Alongside the existing pump-thread collections, add a ranges list and record each node's address per type. Subscribe within the same pump lambda as the existing handlers:

```csharp
                source.Clr.GCGenerationRange += e =>
                {
                    ranges.Add((e.Generation, e.RangeStart, e.RangeStart + e.RangeUsedLength));
                };
```

and extend the existing `GCBulkNode` handler to also record `n.Address` against `n.TypeID`.

**Do not accumulate every address.** A large heap has millions of objects, so a
per-type list of addresses is memory-hungry and pointless — all you need is the
mode. Use a bounded histogram:

```csharp
var generationCounts = new Dictionary<ulong, Dictionary<int, int>>();  // typeId -> generation -> count
```

**One ordering question you must settle by measurement, not assumption:** bucketing
an address at arrival time only works if the ranges are already known. If
`GCGenerationRange` events arrive *after* some `GCBulkNode` events, arrival-time
bucketing silently loses those nodes to `-1`.

Measure it — log the first and last event index of each kind during one collection —
and say in your report which order they actually arrive in. If ranges come last,
buffer the addresses per type id and bucket them once the pump has finished, inside
`Build`, where the existing code already reads pump state safely.

- [ ] **Step 5: Compute the dominant generation in `Build`**

`Build` currently groups by resolved type name and sums counts and bytes. Extend it so each type's `DominantGeneration` is the generation holding the **most instances** of that type, or `-1` when none of its instances mapped to a range.

Remember `Build` aggregates by **name**, not type id — a name may span several type ids, so merge their generation histograms before taking the mode.

- [ ] **Step 6: Run the test**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release --filter CollectAsync_PopulatesDominantGeneration`
Expected: PASS.

Report the actual distribution you observe. A spike measured `gen 0: 2,899 · gen 1: 25,336 · gen 3: 17 · gen 4: 1 · unmapped: 652` across 28,905 nodes; a materially different shape is worth investigating before moving on.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test -c Release`
Expected: all pass. The extra keyword increases event volume — **report the change in collection duration.** Part 1 measured ~150 ms. A large regression here matters, because `snapshot` is interactive.

- [ ] **Step 8: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/HeapCollector.cs tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs
git commit -m "feat: populate DominantGeneration from GC generation ranges

Object addresses were always present on GCBulkNode; nothing published the
generation ranges to bucket them into. Adding the GCHeapSurvivalAndMovement
keyword makes GCGenerationRange fire -- without it the event count is exactly
zero -- and each object's address then buckets into a generation.

Types whose instances map to no range keep the -1 default rather than being
guessed at; roughly 2% of a live heap lands there."
```

---

### Task 5: Exact LOH classification, and ML005

**Files:**
- Modify: `src/MemoryLens.Mcp/Profiler/HeapCollector.cs`
- Modify: `src/MemoryLens.Mcp/Rules/BuiltIn/ML005_ObjectRetainedTooLong.cs`
- Modify: `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs`

**Interfaces:**
- Consumes: `TypeInfo.DominantGeneration` from Task 4.
- Produces: `IsLargeObjectHeap` derived from generation rather than average size.

**Background:** `IsLargeObjectHeap` is currently `avg >= 85_000` — a heuristic, and a reviewer already noted that grouping by name dilutes it (a name spanning one id of huge objects and another of small ones can average below the threshold). **Generation 3 is the Large Object Heap.** The runtime tells us directly.

**A warning about ML005.** Its threshold is `DominantGeneration < 2` → skip. In the spike's measured distribution, **generation 2 held zero objects** — everything was gen 0/1. So populating generations does not automatically make ML005 fire on a short-lived fixture; a process must survive enough collections to promote objects. Do **not** contort the fixture to force ML005 to fire. Assert what is true.

- [ ] **Step 1: Write the failing test**

Add to `HeapCollectorTests.cs`:

```csharp
    [Fact(Timeout = 120_000)]
    public async Task CollectAsync_ClassifiesLohFromGenerationNotSize()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var pid = int.Parse((await stdout.ReadLineAsync(ct))!["READY ".Length..]);

            var data = await new HeapCollector(TestTimeout).CollectAsync(pid, ct);

            // Generation 3 IS the Large Object Heap. Every type that reports gen 3
            // must be flagged, and nothing in gen 0/1 may be.
            foreach (var t in data.Types)
            {
                if (t.DominantGeneration == 3)
                    Assert.True(t.IsLargeObjectHeap, $"{t.FullName} is in gen 3 (LOH) but was not flagged");
                if (t.DominantGeneration is 0 or 1)
                    Assert.False(t.IsLargeObjectHeap, $"{t.FullName} is in gen {t.DominantGeneration} but was flagged as LOH");
            }

            // The fixture retains large double[]; they must land on the LOH.
            var doubles = data.Types.SingleOrDefault(t =>
                string.Equals(t.FullName, "System.Double[]", StringComparison.Ordinal));
            Assert.NotNull(doubles);
            Assert.True(doubles!.IsLargeObjectHeap,
                $"System.Double[] averaged {doubles.TotalBytes / Math.Max(1, doubles.InstanceCount)} bytes " +
                $"in generation {doubles.DominantGeneration} and must be on the LOH");
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }
```

- [ ] **Step 2: Run it and see where it stands**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release --filter CollectAsync_ClassifiesLohFromGenerationNotSize`

It may already partly pass, since the size heuristic and generation 3 often agree. **Report exactly which assertion fails and why** — that tells you whether the heuristic and the truth actually diverge on this fixture.

- [ ] **Step 3: Derive `IsLargeObjectHeap` from generation**

In `Build`, replace the `avg >= 85_000` computation so a type is LOH when its dominant generation is 3. Keep the size heuristic as a **fallback only** for types whose generation is `-1` (unmappable), so the classification never gets worse than it was:

```csharp
            var isLoh = generation == 3 || (generation < 0 && avg >= 85_000);
```

Keep the `LargeObjectHeapBytes` / `LargeObjectCount` rollup driven by whatever `isLoh` ends up being.

- [ ] **Step 4: Run both generation tests**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release --filter CollectAsync`
Expected: all pass, including the Part 1 test that asserts `System.String` is *not* LOH.

- [ ] **Step 5: Remove the "cannot fire" remark from ML005**

`src/MemoryLens.Mcp/Rules/BuiltIn/ML005_ObjectRetainedTooLong.cs` carries an XML `<remarks>` added in Part 1 stating the rule cannot fire because `DominantGeneration` is never populated. That is no longer true. Delete the remark.

**Do not change the rule's logic or its `< 2` threshold.** Whether it fires now depends on the heap, which is correct.

- [ ] **Step 6: Report whether ML005 actually fires**

Run the full suite: `dotnet test -c Release`

Then report, from the pipeline test's output or a temporary probe you remove afterwards, **which rule ids fire on the real heap now**. Part 1 measured ML001 and ML006. If ML005 still does not fire because the fixture holds no gen-2 objects, say so plainly — that is an honest result, not a failure, and it belongs in your report.

- [ ] **Step 7: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/HeapCollector.cs src/MemoryLens.Mcp/Rules/BuiltIn/ML005_ObjectRetainedTooLong.cs tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs
git commit -m "feat: classify the large object heap from generation, not average size

Generation 3 IS the LOH, so the runtime answers directly what the previous
avg >= 85_000 heuristic guessed at -- a guess that grouping by type name also
diluted, since one name can span ids of very different object sizes. The
heuristic survives only as a fallback for objects that map to no range.

ML005's 'cannot fire' remark is removed now that DominantGeneration is real.
Whether it fires depends on the heap holding gen-2 objects, which is correct."
```

---

### Task 6: The nightly tier

**Files:**
- Create: `.github/workflows/e2e.yml`

**Interfaces:**
- Consumes: the Docker image from Task 3.
- Produces: nothing later tasks depend on.

**Background:** The design spec's nightly tier is now small — no profiler download, no licence gate — because collection is in-process. What it verifies is that the **shipped artifacts** start: the Docker image and the npm shim. Neither has ever been executed by CI.

- [ ] **Step 1: Read the existing workflow for its conventions**

Run: `cat .github/workflows/ci.yml`

Note the `permissions` block, how `setup-dotnet` uses `global-json-file`, and how the `alert` job is written — specifically that it uses `always()` with explicit `needs.<job>.result` checks rather than a bare `failure()`, because a skipped dependency would otherwise skip the alert.

- [ ] **Step 2: Create the workflow**

`.github/workflows/e2e.yml`:

```yaml
name: E2E

on:
  schedule:
    - cron: '0 3 * * *'
  workflow_dispatch:

permissions:
  contents: read

jobs:
  docker:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Build the image
        run: docker build -t memorylens-mcp:e2e .

      - name: The server starts and lists exactly its five tools
        run: |
          set -euo pipefail
          out=$(printf '%s\n%s\n%s\n' \
            '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"e2e","version":"1"}}}' \
            '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
            '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
            | docker run -i --rm memorylens-mcp:e2e)
          echo "$out"
          for tool in snapshot compare_snapshots analyze list_processes get_rules; do
            echo "$out" | grep -q "\"$tool\"" || { echo "missing tool: $tool"; exit 1; }
          done
          echo "$out" | grep -q '"ensure_dotmemory"' && { echo "ensure_dotmemory is back"; exit 1; }
          echo "all five tools present, and the removed one is absent"

  npm:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: ./global.json

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'

      - name: The npm shim starts and answers tools/list
        run: |
          set -euo pipefail
          out=$(printf '%s\n%s\n%s\n' \
            '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"e2e","version":"1"}}}' \
            '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
            '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
            | node npm/bin/memorylens-mcp.js)
          echo "$out"
          echo "$out" | grep -q '"get_rules"' || { echo "shim did not list tools"; exit 1; }

  pack:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: ./global.json

      - name: Pack and assert the version stamp fired
        run: |
          set -euo pipefail
          dotnet pack src/MemoryLens.Mcp/MemoryLens.Mcp.csproj -c Release -p:PackageVersion=9.9.9 -o ./artifacts
          cd ./artifacts
          unzip -o -q MemoryLens.Mcp.9.9.9.nupkg -d extracted
          grep -q '"version": "9.9.9"' extracted/.mcp/server.json \
            || { echo "server.json version was not stamped:"; cat extracted/.mcp/server.json; exit 1; }
          echo "version stamp verified"

  alert:
    needs: [docker, npm, pack]
    if: always() && github.event_name == 'schedule'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      issues: write
    env:
      GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
      GH_REPO: ${{ github.repository }}
      RUN_URL: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
    steps:
      - name: Open or update the nightly-failure issue
        if: >-
          needs.docker.result != 'success' ||
          needs.npm.result != 'success' ||
          needs.pack.result != 'success'
        run: |
          set -euo pipefail
          existing=$(gh issue list --label e2e-failure --state open --json number --jq '.[0].number // empty')
          body=$(printf 'Nightly E2E failed.\n\ndocker: %s\nnpm: %s\npack: %s\n\nRun: %s\n' \
            "${{ needs.docker.result }}" "${{ needs.npm.result }}" "${{ needs.pack.result }}" "$RUN_URL")
          if [ -n "$existing" ]; then
            gh issue comment "$existing" --body "$body"
          else
            gh issue create --title "Nightly E2E failed" --label e2e-failure --body "$body"
          fi
```

Note the alert conditions use `!= 'success'` rather than `== 'failure'`, matching `ci.yml` — a cancelled or skipped job must still alert.

- [ ] **Step 3: Create the label**

```bash
gh label create e2e-failure --color b60205 --description "Nightly E2E verification failed"
```

Verify: `gh label list | grep e2e-failure`

- [ ] **Step 4: Verify the YAML parses and the job graph is right**

```bash
python -c "import yaml; yaml.safe_load(open('.github/workflows/e2e.yml')); print('ok')"
python -c "import yaml; d=yaml.safe_load(open('.github/workflows/e2e.yml')); [print(k, '| needs:', v.get('needs')) for k,v in d['jobs'].items()]"
```

Expected: `ok`, then four jobs — `docker`, `npm`, `pack` with no needs, and `alert` needing all three.

- [ ] **Step 5: Run it once manually before trusting it**

This workflow has never executed. `workflow_dispatch` cannot be triggered until the file is on the default branch, so this step happens **after** the PR merges. Record in your report that it must be run once via `gh workflow run e2e.yml` and its result checked — a nightly that has never been executed is not a verification, it is a hope.

If any job fails on that first run, that is a real finding about a shipped artifact, not a workflow bug.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/e2e.yml
git commit -m "ci: verify the shipped artifacts nightly

Neither the Docker image nor the npm shim has ever been executed by CI, so
nothing catches a packaging change that stops the server starting. This runs
both through the real MCP handshake and asserts the tool manifest, and checks
that dotnet pack still stamps the version into .mcp/server.json.

Asserts ensure_dotmemory is absent as well as that the five real tools are
present, so a resurrected tool fails rather than passing silently.

The alert uses always() with explicit result checks, matching ci.yml: a
cancelled or skipped job must still alert."
```

---

### Task 7: Recalibrate the floors

**Files:**
- Modify: `tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj`
- Modify: `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj` (only if its count changed)

**Interfaces:**
- Consumes: the tests added in Tasks 4 and 5.
- Produces: nothing.

**Background:** Both projects carry a `--minimum-expected-tests` floor that must track the real count. Tasks 4 and 5 added integration tests.

- [ ] **Step 1: Get the real counts**

Run: `dotnet test -c Release`

Read the ACTUAL per-project totals from the output. **Do not compute them from this plan** — earlier phases were caught out by exactly that.

- [ ] **Step 2: Set each floor to actual minus 2**

Update `TestingPlatformCommandLineArguments` in each csproj whose count changed.

- [ ] **Step 3: Prove each floor fires**

For **each** project: temporarily set its floor above the actual count, run that project alone, and confirm it FAILS with exit code 9 and the shape `error: 1, failed: 0`. Then restore and confirm PASS. Paste all four observations.

- [ ] **Step 4: Commit**

```bash
git add tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj
git commit -m "test: recalibrate the discovery floors after the generation tests

Both floors set from a real run rather than arithmetic, and both verified to
fire in the failing direction."
```

---

## Done when

- `grep -rni dotmemory` returns hits only in `CHANGELOG.md` and historical design documents under `docs/plans/`.
- The Docker image builds on `dotnet/runtime:10.0` and its server answers `tools/list` with exactly five tools.
- `TypeInfo.DominantGeneration` carries real generations for the majority of a live heap.
- `IsLargeObjectHeap` follows generation 3, with the size heuristic only as a fallback for unmappable types.
- ML005 no longer claims it cannot fire.
- `.github/workflows/e2e.yml` exists, and its first manual run has been checked.
- `dotnet test -c Release` passes with both floors active and proven.

## After the branch merges

**Run the nightly workflow once manually** (`gh workflow run e2e.yml`) and check the result. Until that happens it has never executed.

**Then, and only then, merge the release-please PR.** It publishes 2.0.0 to NuGet, npm and Docker. Everything that made that unsafe is fixed by this branch — which is precisely why the release should wait for it.
