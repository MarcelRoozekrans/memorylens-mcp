# In-Process Heap Collection — Design

**Date:** 2026-08-22
**Status:** Approved for planning
**Supersedes:** Phase 3 of `2026-08-21-testing-rig-hardening-design.md`
**Breaking:** yes — removes an MCP tool, targets 2.0.0

## Context

`analyze` — one of six MCP tools, and the one the README's headline workflow
depends on — does not work on any machine. Three stacked defects, each verified
against real tools and real output rather than inferred from code. Tracked as
issue #161.

### Defect 1 — the tool it shells out to is never installed

`DotMemoryAnalyzer` runs `dotnet-gcdump report <path>`. Nothing in this product
installs `dotnet-gcdump`: `ensure_dotmemory` installs JetBrains dotMemory
Console, a different tool; the word "gcdump" appears nowhere in `README.md` or
`docs/`; and README Prerequisites states "no manual installation required."

Reproduced by driving the real server over the real MCP protocol:

```
System.ComponentModel.Win32Exception (2): An error occurred trying to start
process 'dotnet-gcdump'. The system cannot find the file specified.
   at MemoryLens.Mcp.Analysis.DotMemoryAnalyzer.AnalyzeSnapshotAsync(...) :line 14
   at MemoryLens.Mcp.Tools.AnalyzeTool.analyze(...) :line 28
```

The documented fallback at `DotMemoryAnalyzer.cs:6-7` ("falls back to basic
parsing … if gcdump is unavailable") does not exist. The code only guards
`ExitCode != 0`, but a missing executable throws before any exit code exists.

### Defect 2 — the parser expects a format gcdump never emits

Installing the tool does not help. `GcDumpReportParser` matches
`^\s*[0-9a-fA-F]+\s+(?<count>\d+)\s+(?<size>\d+)\s+(?<name>.+)$` — an `MT` hex
column, then count, then size. Real `dotnet-gcdump report` output is:

```
        851,994  GC Heap bytes
   Object Bytes     Count  Type
         49,200         1  System.Int32[] (Bytes > 10K)  [System.Private.CoreLib.dll]
```

No MT column, bytes first, thousands separators, no `Total` line. Feeding 1,538
lines of real output through this repo's own parser yields **0 types, 0 bytes**.

### Defect 3 — even with the right tool, 88.7% of the heap is silently dropped

The parser's documented format is actually `dotnet-dump`'s SOS `dumpheap -stat`.
Pointing it at that tool still fails, because `dotnet-dump` formats numbers with
thousands separators above 999 and the regex uses `\d+`:

```
data rows       : 1596
parsed by regex : 1477
DROPPED         : 119     <- every one because of a comma

true bytes across all rows : 1,760,135
bytes the parser sees      :   199,348
SILENTLY LOST              : 1,560,787  (88.7%)
```

The dropped rows are the largest ones — `System.String` at 301 KB, `Free` at
316 KB. A leak analyzer that discards exactly what a leak looks like.

Defect 3 is the dangerous one: it is the only one that produces a plausible
wrong answer instead of an obvious failure. Fixing defect 1 alone would turn a
loud crash into a quiet lie — `analyze` reporting "no memory issues found" on a
leaking heap.

### Why 152 tests are green

Every parser and rule test feeds hand-written text in the format the parser
expects — a format no tool in the pipeline emits. The parser is thoroughly
tested against a fiction, and the rule engine against that same fiction.

Same fixture-shaped hole as #118, but deeper: there the fake stood in for a
*behaviour*; here it stands in for the *data itself*.

### A structural problem the fix must resolve

`snapshot` produces a dotMemory `.dmw` workspace. **dotMemory Console cannot
analyze anything** — `dotMemory help` lists only capture commands
(`get-snapshot`, `attach`, `start`, …) plus utilities, and `.dmw` requires the
paid standalone GUI, as its own nuspec states. So even a perfect `analyze` could
never read what `snapshot` writes. Capture and analysis must produce and consume
the same artifact.

## Decision

Collect heap data **in-process** via EventPipe. No external profiler, no
downloaded tool, no text parsing.

### Spike evidence

Verified before this design was written, not assumed:

- **Reading a `.gcdump` file is impossible via public API.** TraceEvent 3.2.6
  exposes no `GCHeapDump`, `MemoryGraph`, or `Graphs` namespace;
  `Microsoft.Diagnostics.NETCore.Client` has no heap-dump types; and the graph
  code ships only inside the `dotnet-gcdump` **tool** package, never as a library.
- **Collecting in-process is fully supported on public API.**
  `ClrTraceEventParser.Keywords` publicly exposes `GCHeapDump`, `GCHeapCollect`,
  `GCHeapAndTypeNames` and `Type` — the same keywords `dotnet-gcdump` uses.
  `GCBulkTypeTraceData` gives typeId→name; `GCBulkNodeTraceData` gives objects
  and sizes; `GCBulkEdgeTraceData` gives references.

A ~40-line probe against a live process produced correct, named, per-type
statistics:

```
types named : 1877   types with objects : 1561
total objects: 13,524   total bytes: 1,070,044
   213,396   2,833  System.String
    75,944     863  System.Reflection.RuntimeParameterInfo
```

Cross-checked against `dotnet-dump`'s independent `dumpheap -stat` on the same
process: `RuntimeParameterInfo` = **863 objects, 75,944 bytes**. Exact match
from a separate tool.

### Why this rather than repairing the shell-out

Every defect in #161 is a text-format defect. In-process collection makes that
class **structurally impossible** — no text, no format, no external binary to be
absent or to change its output between versions.

## Goals

1. `analyze` returns real findings from a real heap.
2. Remove every runtime external dependency from the analysis path.
3. Make "collected nothing" impossible to render as "found nothing".

## Non-goals

- Reference-graph / retention analysis. The current ten rules need per-type
  counts and bytes. Edges are available for later; not now.
- Exporting `.gcdump` or `.dmw` for other tools. Nothing needs it today, and the
  library that writes `.gcdump` is the unpublished API the spike ruled out.
- Rewriting the rule engine, the rules, config loading, or the MCP tool layer.

## Section 1 — Architecture

```
   snapshot(pid) ──▶ HeapCollector (new, in-process)
                     DiagnosticsClient → EventPipe
                     GCBulkType → typeId → name
                     GCBulkNode → count + bytes per type
                          │
                          ▼
                    SnapshotData  (existing type, unchanged)
                          │
                          ▼
                    SnapshotStore (new) ──▶ <dir>/<id>.json
                          │
   analyze(id) ─────▶ AnalysisEngine ──▶ Rules ML001–ML010 ──▶ findings
```

`SnapshotData`, `TypeInfo`, `HeapInfo`, `AnalysisEngine`, all ten rules,
`ConfigLoader`, and the MCP tool layer are untouched. The change is confined to
how `SnapshotData` is populated — exactly where all three defects live.

### Deleted

| File | Lines |
|---|---:|
| `Profiler/DotMemoryToolManager.cs` | 379 |
| `Profiler/DotMemoryAutoInstaller.cs` | 290 |
| `Profiler/SnapshotManager.cs` | 141 |
| `Analysis/GcDumpReportParser.cs` | 133 |
| `Analysis/DotMemoryAnalyzer.cs` | 69 |
| `Tools/EnsureDotMemoryTool.cs`, `IDotMemoryAutoInstaller.cs`, `IDotMemoryAnalyzer.cs` | 43 |
| **product total** | **~1,055** |
| `DotMemoryToolManagerTests` + `DotMemoryAutoInstallerTests` | 369 |
| `ExecuteBitTests` + `ExecChainTests` + `PackageFixtureBuilder` | 286 |

### Retained

`ProcessRunner` / `IProcessRunner` — `snapshot`'s `command` parameter still
launches a target. `DiagnosticPortProcessLister` — already uses the same
diagnostics IPC, unaffected. `McpStdioClient`, `TempDir`, `McpProtocolTests`,
`ConfigLoadingTests` — untouched.

### Recorded consequence

The #118 execute-bit guards merged on 2026-08-22 (`ExecuteBitTests`,
`ExecChainTests`, `PackageFixtureBuilder`) are deleted here. This is not waste:
deleting `DotMemoryAutoInstaller` deletes the bug class those tests guard, which
is strictly stronger than guarding it. Recorded so the deletion is not later
read as an accident.

## Section 2 — `HeapCollector`

```csharp
public interface IHeapCollector
{
    Task<SnapshotData> CollectAsync(int pid, CancellationToken ct);
}
```

One method, one responsibility, returning the existing type — so everything
downstream is unchanged and the collector stays swappable for a fake in rule
tests.

### Mechanism

Start an EventPipe session on the target with
`GC | GCHeapCollect | GCHeapDump | GCHeapAndTypeNames | Type`. The runtime
induces a GC and streams the heap. Subscribe to:

- `GCBulkType` → `typeId → name`
- `GCBulkNode` → per-object type and size, aggregated to count and bytes per type

The `IsLikelyDisposable` / `IsLikelyFinalizable` heuristics currently in
`GcDumpReportParser` are **lifted into a new `TypeClassifier`, not deleted** —
they are name-based classification independent of the data source, and the rules
depend on them.

### Session lifecycle — the real risk

The spike printed `TIMEOUT`. The data was complete and correct, but the session
was never stopped, so `source.Process()` blocked forever. There is no obvious
"heap dump complete" event to await. The collector therefore needs:

1. A stop condition on the GC-end marker following the bulk-node stream, then
   `session.Stop()`.
2. An overall timeout (default 30s, matching `dotnet-gcdump`) that stops the
   session and throws `HeapCollectionTimeoutException` carrying the node and
   type event counts seen so far.
3. `CancellationToken` honoured by stopping the session — the same lesson as
   `McpStdioClient`, where cancelling a blocked read did not work and the real
   bound had to come from outside.

### Failure behaviour

| Situation | Behaviour |
|---|---|
| PID does not exist / already exited | Throw naming the pid, not a raw `ServerNotAvailableException` |
| Target is not a .NET process | Clear message — no diagnostic endpoint |
| Insufficient permissions | Explicit message (the common Linux/container case) |
| Timeout | Throw with partial-progress counts |
| Zero objects collected | **Throw. Never return empty.** |

The last row is the direct lesson of #161. The old code returned
`new SnapshotData()` on failure, so a broken pipeline rendered as "no memory
issues found". An empty heap is never a real answer.

### Dependencies

`Microsoft.Diagnostics.NETCore.Client` (small) and
`Microsoft.Diagnostics.Tracing.TraceEvent` (large; also pulls native
PDB-reading components). Both Microsoft-published and actively maintained.
TraceEvent's size is a real cost against ~1,055 lines deleted and one fewer
runtime download; the trade is judged worthwhile because it removes a network
dependency from the product's critical path.

## Section 3 — Tool surface, storage, identity

### `SnapshotStore`

```csharp
public interface ISnapshotStore
{
    Task<string> SaveAsync(SnapshotData data, CancellationToken ct);   // returns snapshotId
    Task<SnapshotData> LoadAsync(string snapshotId, CancellationToken ct);
}
```

Writes `<temp>/memorylens-snapshots/<id>.json` — the same directory and the same
8-character `Guid` ID scheme `SnapshotManager` uses today, so nothing
user-visible about IDs changes. Plain `System.Text.Json`.

Snapshots become small: per-type aggregates, not object graphs. The spike's
1,561 types serialize to roughly 150 KB, against 668 KB for a gcdump and 149 MB
for a full process dump.

### Tools

| Tool | Change |
|---|---|
| `snapshot` | Same parameters; collects in-process, persists JSON. Works. |
| `analyze` | Same parameters; `snapshotPath` still accepted, also accepts a bare ID. Works. |
| `compare_snapshots` | Same parameters; two collections, existing delta logic unchanged. |
| `list_processes` | Unchanged. |
| `get_rules` | Unchanged. |
| `ensure_dotmemory` | **Removed.** |

`analyze` keeps accepting `snapshotPath` so existing prompts keep working.

`snapshot`'s `command` parameter needs the collector to attach *after* the
process starts. `ProcessRunner` survives for this, and it introduces a race the
E2E test must cover: start the process, wait until it has a diagnostic endpoint,
then collect.

### Identity

Three places claim dotMemory and all become false:

- `README.md` — "wraps JetBrains dotMemory", the entire "dotMemory CLI
  Installation" section, and its platform-support table
- `MemoryLens.Mcp.csproj` — `<Description>` and the `dotmemory` package tag
- `.mcp/server.json` — "wraps JetBrains dotnet-dotmemory with heuristic-based
  analysis rules"

Replacement wording: *"MCP server for .NET memory profiling — collects heap
snapshots in-process via EventPipe and applies a heuristic rule engine."*

The name `MemoryLens` is unaffected. The product gains zero external
dependencies rather than losing a marquee one: it now profiles any .NET process
with nothing installed.

`docs/docker.md` loses its section on mounting a volume so a runtime profiler
download survives `--rm`. The image becomes self-contained.

### Versioning

Removing a tool is breaking. With release-please and Conventional Commits this
is `feat!:` → **2.0.0**, from the current 1.7.2. An honest major, rather than a
tool removal hidden in a minor.

## Section 4 — Testing

Because collection needs no download and no external tool, most of what the
previous spec scheduled as a nightly tier becomes **hermetic, fast, and runnable
on every PR across all three OSes**. The JetBrains licence question that gated
Phase 3 is moot: nothing downloads dotMemory.

### Integration tier — every PR, ubuntu + macOS + Windows

The `LeakyApp` fixture from the previous spec moves here: a console app
retaining a growing static collection, duplicated strings, undisposed streams
and closure display-classes, driven over stdin
(`READY <pid>` → `grow` → `GROWN` → `exit`).

| Test | Proves |
|---|---|
| Collect from live `LeakyApp` | Real EventPipe collection returns named types with plausible counts and bytes |
| **Full pipeline: collect → store → analyze** | **Specific rule IDs fire on a real heap.** The test that would have caught #161 |
| `compare_snapshots` across a `grow` | Deltas reflect real growth, not fixture text |
| Empty collection **throws** | The collector refuses to report "no issues" having collected nothing |
| Dead PID / non-.NET target | Actionable message, not a raw `ServerNotAvailableException` |
| Timeout is bounded | A wedged collection fails instead of hanging CI |
| `SnapshotStore` round-trip | Save→load preserves the data |

Row 4 is the regression guard for the *shape* of #161, not just its mechanism.

### Existing suite

The ten rule classes, `AnalysisEngineTests`, `ConfigLoaderTests`,
`McpProtocolTests` and `ConfigLoadingTests` survive unchanged — all operate on
`SnapshotData`, which is not changing. `GcDumpReportParserTests` is deleted with
its parser; its `IsLikelyDisposable` / `IsLikelyFinalizable` cases move to
`TypeClassifierTests`.

`--minimum-expected-tests` floors must be recalibrated in the same commit that
moves or deletes tests, per the Phase 2 handoff note.

### Nightly tier

`e2e.yml` keeps only what cannot run in the PR gate: `docker build` and run, the
npm shim, and the pack / `server.json` version assertion. No profiler download,
no licence gate. The Docker test gets stronger — the container can now take a
real snapshot, because there is nothing to download at runtime.

### Recorded gap

`HeapCollector` cannot be meaningfully unit-tested; it needs a real process with
a real diagnostic endpoint. Its coverage is integration-only by nature. Stated
explicitly rather than papered over with a mock that proves nothing — which is
precisely how `FakeProcessRunner` let both #118 and #161 through.

## Risks

**EventPipe session lifecycle.** The one part with no prior art in this repo,
and the spike's only failure. Mitigated by the bounded timeout, the explicit
stop condition, and integration tests that exercise both.

**Permissions on Linux and in containers.** Attaching to a diagnostic endpoint
can require matching UID or `SYS_PTRACE`. The existing `docs/docker.md` already
documents `--pid=host --cap-add=SYS_PTRACE` for profiling from a container; the
same constraints apply, and the collector must report permission failures
clearly rather than as an empty result.

**TraceEvent package weight.** Grows the Docker image. Accepted against removing
a ~100 MB runtime download.

**Breaking change.** `ensure_dotmemory` disappears; clients referencing it by
name get an unknown-tool error. Mitigated by the major version bump and README
rewrite; not mitigated by a deprecation shim, deliberately — a no-op tool that
pretends to install something would be its own small lie.
