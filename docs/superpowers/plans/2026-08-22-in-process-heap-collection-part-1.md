# In-Process Heap Collection (Part 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `analyze` actually work, by collecting heap data in-process via EventPipe instead of shelling out to a tool that is never installed and parsing a format nothing emits.

**Architecture:** A new `HeapCollector` starts an EventPipe session against a target PID, aggregates `GCBulkType` and `GCBulkNode` events into the existing `SnapshotData` type, and stops on a live completion signal. `SnapshotStore` persists that as JSON. The rule engine, all ten rules, and the MCP tool layer are unchanged — only the way `SnapshotData` gets populated changes. The entire dotMemory path is then deleted.

**Tech Stack:** .NET 10 (`net10.0`), `Microsoft.Diagnostics.NETCore.Client`, `Microsoft.Diagnostics.Tracing.TraceEvent`, xunit.v3 4.0.0, Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-08-22-in-process-heap-collection-design.md`

## Global Constraints

- Target framework is `net10.0` for every project. Do not change it.
- **OS-conditional tests must branch INSIDE the test body with an early `return`, never xunit's `Skip =`.** A skipped test may not count toward `--minimum-expected-tests`, which fails the Windows CI leg against a floor calibrated on Linux.
- The required branch-protection status check is named exactly **`build`**. Do not rename it or the `test` matrix job in `.github/workflows/ci.yml`.
- `global.json` must keep its SDK pin (`10.0.400` / `latestPatch`) and `"test": {"runner": "Microsoft.Testing.Platform"}`. Do not edit it.
- **`HeapCollector` must never return an empty `SnapshotData`.** Collecting nothing is a failure and must throw. This is the direct lesson of #161, where an empty result rendered as "no memory issues found" on a leaking heap.
- Every spawned process gets a hard timeout and is killed on dispose. Every test gets its own temp directory. No `Thread.Sleep` in tests, no wall-clock ordering.
- Conventional Commits. This part contains a breaking change (`ensure_dotmemory` removal) — use `feat!:` on that commit so release-please targets 2.0.0.
- Do not rewrite the rule engine, the ten rules, `ConfigLoader`, or `SnapshotData`/`TypeInfo`/`HeapInfo`.

## Verified Facts (established by spike before this plan — do not re-derive)

1. **EventPipe collection works and the data is correct.** A probe against a live process produced 1,561 types with named per-type counts and bytes. Cross-checked against `dotnet-dump`'s independent `dumpheap -stat` on the same process: `System.Reflection.RuntimeParameterInfo` = 863 objects, 75,944 bytes — exact match.

2. **Completion detection**, read from `dotnet-gcdump`'s `EventPipeDotNetHeapDumper`:
   - Capture a GC number from the first `GCStart` where `Depth == 2 && Type != GCType.BackgroundGC`.
   - The dump is complete when a `GCStop` arrives whose `Count` equals that number.
   - Defensive exits: cancellation; reader task completed; no EventPipe data within 5s (target has no .NET heap); overall timeout, default 30s.

3. **The buffer size is load-bearing.** `dotnet-gcdump` requests **1024 MB**. With the default buffer, events do NOT stream live and appear only after `session.Stop()` — two separate probes hung on exactly this. Use `circularBufferMB: 1024`.

4. **Keywords:** the composite `ClrTraceEventParser.Keywords.GCHeapSnapshot`, not hand-assembled individual flags.

5. **Existing data contract** (`src/MemoryLens.Mcp/Analysis/SnapshotData.cs`) — do not change:
   ```csharp
   public class SnapshotData { public IList<TypeInfo> Types { get; init; } = []; public HeapInfo Heap { get; init; } = new(); }
   public class TypeInfo { public required string FullName { get; init; } public int InstanceCount { get; init; }
       public long TotalBytes { get; init; } public bool ImplementsIDisposable { get; init; }
       public bool HasFinalizer { get; init; } public int DominantGeneration { get; init; } = -1;
       public bool IsLargeObjectHeap { get; init; } }
   public class HeapInfo { public long TotalBytes { get; init; } public long LargeObjectHeapBytes { get; init; } public int LargeObjectCount { get; init; } }
   ```

6. **`AnalysisEngine.EnrichContextAsync`** is the only consumer of `IDotMemoryAnalyzer`. It calls `AnalyzeSnapshotAsync(path, ct)` for single snapshots and `CompareSnapshotsAsync(beforePath, afterPath, ct)` for comparisons, assigning `context with { Data = ... }` / `{ Data = comparison.After, Comparison = comparison }`. That is the seam to swap.

7. **`ComputeDeltas`** lives in `DotMemoryAnalyzer.cs` and is the only delta logic in the product. It must be **moved, not deleted**.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/MemoryLens.Mcp/Analysis/TypeClassifier.cs` | Create | Name-based `IsLikelyDisposable` / `IsLikelyFinalizable`, lifted from the parser |
| `src/MemoryLens.Mcp/Profiler/SnapshotStore.cs` | Create | `ISnapshotStore` + JSON persistence |
| `src/MemoryLens.Mcp/Profiler/HeapCollector.cs` | Create | `IHeapCollector` + EventPipe collection |
| `src/MemoryLens.Mcp/Analysis/SnapshotReader.cs` | Create | `ISnapshotReader`, replaces `IDotMemoryAnalyzer`; carries `ComputeDeltas` |
| `src/MemoryLens.Mcp/Analysis/AnalysisEngine.cs` | Modify | Swap `IDotMemoryAnalyzer` → `ISnapshotReader` |
| `src/MemoryLens.Mcp/Tools/SnapshotTool.cs` | Modify | Use collector + store |
| `src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs` | Modify | Two collections + store |
| `src/MemoryLens.Mcp/Program.cs` | Modify | DI rewiring; drop dotMemory registrations |
| `tests/MemoryLens.Mcp.LeakyApp/` | Create | Deliberately-leaking fixture app |
| `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs` | Create | Real collection against `LeakyApp` |
| `tests/MemoryLens.Mcp.IntegrationTests/PipelineTests.cs` | Create | collect → store → analyze, asserting rule IDs |
| `src/MemoryLens.Mcp/Profiler/DotMemory*.cs`, `SnapshotManager.cs`, `Analysis/DotMemoryAnalyzer.cs`, `Analysis/GcDumpReportParser.cs`, `Tools/EnsureDotMemoryTool.cs` | **Delete** | The dotMemory path |

## Before you start

```bash
git fetch origin
git checkout -b feat/in-process-heap-collection origin/main
```

Everywhere below, "the working branch" means `feat/in-process-heap-collection`.

---

### Task 1: TypeClassifier and SnapshotStore

**Files:**
- Create: `src/MemoryLens.Mcp/Analysis/TypeClassifier.cs`
- Create: `src/MemoryLens.Mcp/Profiler/SnapshotStore.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Analysis/TypeClassifierTests.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/SnapshotStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public static class TypeClassifier` with `public static bool IsLikelyDisposable(string typeName)` and `public static bool IsLikelyFinalizable(string typeName)`.
  - `public interface ISnapshotStore` with `Task<string> SaveAsync(SnapshotData data, CancellationToken ct)` (returns the snapshot id) and `Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct)`.
  - `public sealed class SnapshotStore : ISnapshotStore` with constructor `SnapshotStore(string? rootDirectory = null)`.
  - Tasks 3–6 consume both.

**Background:** These are the two pieces with no EventPipe dependency, so they are genuinely unit-testable. `TypeClassifier` is lifted verbatim from `GcDumpReportParser` — the rules depend on these heuristics and they are independent of the data source, so they must survive the parser's deletion.

- [ ] **Step 1: Create TypeClassifier by lifting the heuristics**

Open `src/MemoryLens.Mcp/Analysis/GcDumpReportParser.cs` and copy the `KnownDisposableTypes` set and the two private methods verbatim. Create `src/MemoryLens.Mcp/Analysis/TypeClassifier.cs`:

```csharp
namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Name-based classification of heap types. Independent of how the heap was
/// collected, so it outlives the report parser it was lifted from.
/// </summary>
public static class TypeClassifier
{
    private static readonly HashSet<string> KnownDisposableTypes =
    [
        "System.IO.FileStream",
        "System.IO.StreamReader",
        "System.IO.StreamWriter",
        "System.IO.MemoryStream",
        "System.IO.BinaryReader",
        "System.IO.BinaryWriter",
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpResponseMessage",
        "System.Net.Http.HttpRequestMessage",
        "System.Net.Sockets.Socket",
        "System.Net.Sockets.TcpClient",
        "System.Net.Sockets.TcpListener",
        "System.Data.SqlClient.SqlConnection",
        "System.Data.SqlClient.SqlCommand",
        "System.Data.SqlClient.SqlDataReader",
        "Microsoft.Data.SqlClient.SqlConnection",
        "Microsoft.Data.SqlClient.SqlCommand",
        "Microsoft.Data.SqlClient.SqlDataReader",
        "System.Threading.CancellationTokenSource",
        "System.Threading.Timer",
        "System.Threading.SemaphoreSlim",
        "System.Threading.ManualResetEventSlim",
        "System.Security.Cryptography.RSA",
        "System.Security.Cryptography.Aes",
    ];

    public static bool IsLikelyDisposable(string typeName)
    {
        if (KnownDisposableTypes.Contains(typeName))
            return true;

        return typeName.Contains("Stream", StringComparison.Ordinal)
            || typeName.Contains("Connection", StringComparison.Ordinal)
            || typeName.Contains("Reader", StringComparison.Ordinal)
            || typeName.Contains("Writer", StringComparison.Ordinal)
            || typeName.Contains("Client", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Handle", StringComparison.Ordinal);
    }

    public static bool IsLikelyFinalizable(string typeName)
    {
        return typeName.Contains("SafeHandle", StringComparison.Ordinal)
            || typeName.Contains("FileStream", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Timer", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Write TypeClassifier tests**

`tests/MemoryLens.Mcp.Tests/Analysis/TypeClassifierTests.cs`:

```csharp
using MemoryLens.Mcp.Analysis;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class TypeClassifierTests
{
    [Fact]
    public void IsLikelyDisposable_KnownType_ReturnsTrue()
    {
        Assert.True(TypeClassifier.IsLikelyDisposable("System.IO.FileStream"));
        Assert.True(TypeClassifier.IsLikelyDisposable("System.Threading.Timer"));
    }

    [Fact]
    public void IsLikelyDisposable_HeuristicMatch_ReturnsTrue()
    {
        Assert.True(TypeClassifier.IsLikelyDisposable("MyApp.Data.DbConnection"));
        Assert.True(TypeClassifier.IsLikelyDisposable("MyApp.Io.CustomWriter"));
    }

    [Fact]
    public void IsLikelyDisposable_PlainType_ReturnsFalse()
    {
        Assert.False(TypeClassifier.IsLikelyDisposable("System.String"));
        Assert.False(TypeClassifier.IsLikelyDisposable("MyApp.Models.Customer"));
    }

    [Fact]
    public void IsLikelyFinalizable_MatchesKnownPatterns()
    {
        Assert.True(TypeClassifier.IsLikelyFinalizable("Microsoft.Win32.SafeHandles.SafeFileHandle"));
        Assert.True(TypeClassifier.IsLikelyFinalizable("System.Threading.Timer"));
        Assert.False(TypeClassifier.IsLikelyFinalizable("System.String"));
    }
}
```

- [ ] **Step 3: Create SnapshotStore**

`src/MemoryLens.Mcp/Profiler/SnapshotStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;

namespace MemoryLens.Mcp.Profiler;

public interface ISnapshotStore
{
    /// <summary>Persists a snapshot and returns its generated id.</summary>
    Task<string> SaveAsync(SnapshotData data, CancellationToken ct);

    /// <summary>Loads a snapshot by id, or by full path to a snapshot file.</summary>
    Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct);
}

/// <summary>
/// Persists snapshots as JSON under a snapshot directory. Snapshots are per-type
/// aggregates, not object graphs, so they stay small.
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly string _root;

    public SnapshotStore(string? rootDirectory = null)
    {
        _root = rootDirectory
            ?? Path.Combine(Path.GetTempPath(), "memorylens-snapshots");
    }

    public string PathFor(string snapshotId) => Path.Combine(_root, snapshotId + ".json");

    public async Task<string> SaveAsync(SnapshotData data, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);

        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var stream = File.Create(PathFor(id));
        await using (stream.ConfigureAwait(false))
            await JsonSerializer.SerializeAsync(stream, data, Options, ct).ConfigureAwait(false);

        return id;
    }

    public async Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct)
    {
        var path = File.Exists(idOrPath) ? idOrPath : PathFor(idOrPath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Snapshot '{idOrPath}' not found (looked in '{path}').", path);

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<SnapshotData>(stream, Options, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Snapshot '{idOrPath}' deserialized to null.");
        }
    }
}
```

`PathFor` is public because Task 4's `SnapshotTool` returns the path to callers, preserving today's `SnapshotResult.SnapshotPath` contract.

- [ ] **Step 4: Write SnapshotStore tests**

`tests/MemoryLens.Mcp.Tests/Profiler/SnapshotStoreTests.cs`:

```csharp
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotStoreTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-store-" + Guid.NewGuid().ToString("N"));

    private static SnapshotData Sample() => new()
    {
        Types =
        [
            new TypeInfo { FullName = "System.String", InstanceCount = 1000, TotalBytes = 40000 },
            new TypeInfo { FullName = "MyApp.Thing", InstanceCount = 7, TotalBytes = 168, ImplementsIDisposable = true },
        ],
        Heap = new HeapInfo { TotalBytes = 40168, LargeObjectHeapBytes = 0, LargeObjectCount = 0 },
    };

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var id = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(id, TestContext.Current.CancellationToken);

            Assert.Equal(2, loaded.Types.Count);
            Assert.Equal("System.String", loaded.Types[0].FullName);
            Assert.Equal(1000, loaded.Types[0].InstanceCount);
            Assert.Equal(40000, loaded.Types[0].TotalBytes);
            Assert.True(loaded.Types[1].ImplementsIDisposable);
            Assert.Equal(40168, loaded.Heap.TotalBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LoadAsync_AcceptsAFullPath()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var id = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(store.PathFor(id), TestContext.Current.CancellationToken);

            Assert.Equal(2, loaded.Types.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LoadAsync_UnknownId_ThrowsWithTheIdInTheMessage()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(
                () => store.LoadAsync("nosuchid", TestContext.Current.CancellationToken));

            Assert.Contains("nosuchid", ex.Message, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SaveAsync_ReturnsDistinctIdsForEachSnapshot()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var a = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
            var b = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            Assert.NotEqual(a, b);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
```

- [ ] **Step 5: Run the unit suite**

Run: `dotnet test tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj -c Release`
Expected: `Passed!` with `total: 145` (137 existing + 4 TypeClassifier + 4 SnapshotStore).

If the count differs, record the ACTUAL number — later tasks recalibrate the floor from real counts, not from this plan's arithmetic.

- [ ] **Step 6: Commit**

```bash
git add src/MemoryLens.Mcp/Analysis/TypeClassifier.cs src/MemoryLens.Mcp/Profiler/SnapshotStore.cs tests/MemoryLens.Mcp.Tests
git commit -m "feat: add TypeClassifier and SnapshotStore

TypeClassifier lifts the name-based disposable/finalizable heuristics out of
GcDumpReportParser. The rules depend on them and they are independent of how the
heap was collected, so they must outlive the parser.

SnapshotStore persists snapshots as JSON keyed by an 8-character id, in the same
directory and with the same id scheme the old SnapshotManager used, so nothing
user-visible about snapshot ids changes."
```

---

### Task 2: The LeakyApp fixture

**Files:**
- Create: `tests/MemoryLens.Mcp.LeakyApp/MemoryLens.Mcp.LeakyApp.csproj`
- Create: `tests/MemoryLens.Mcp.LeakyApp/Program.cs`
- Create: `tests/MemoryLens.Mcp.IntegrationTests/LeakyAppFixtureTests.cs`
- Modify: `memorylens-mcp.slnx`
- Modify: `tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj`

**Interfaces:**
- Consumes: `MemoryLens.Mcp.TestSupport.TempDir` (exists; `sealed class`, parameterless ctor, `string Path`).
- Produces: a built `MemoryLens.Mcp.LeakyApp.dll` in the integration test output directory, launchable with `dotnet <path>`, plus the stdin protocol below. Tasks 3 and 6 depend on it.

**Background:** `HeapCollector` cannot be unit-tested — it needs a real process with a real diagnostic endpoint. This fixture is that process, and it holds objects the ten rules are written to detect, so Task 6 can assert on specific rule IDs rather than "count > 0".

**The stdin protocol** (Task 3 and Task 6 both drive it):

```
stdout: READY <pid>
stdin:  grow  -> retain another tranche -> stdout: GROWN
stdin:  exit  -> clean shutdown
```

- [ ] **Step 1: Create the fixture project**

`tests/MemoryLens.Mcp.LeakyApp/MemoryLens.Mcp.LeakyApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Write the fixture**

`tests/MemoryLens.Mcp.LeakyApp/Program.cs`:

```csharp
using System.Globalization;

// A process that leaks on purpose, in shapes the ML rules detect.
// Driven over stdin so a test can make it grow deterministically -- no sleeps,
// no wall-clock races.

// ML002: a static collection that only ever grows.
var retained = new List<object>();

// ML010: many duplicate strings that could be interned.
var duplicates = new List<string>();

// ML003 / ML009: disposables that are never disposed.
var streams = new List<MemoryStream>();

void Grow(int tranche)
{
    for (var i = 0; i < 20_000; i++)
        duplicates.Add("memorylens-leak-" + (i % 200).ToString(CultureInfo.InvariantCulture));

    for (var i = 0; i < 500; i++)
        streams.Add(new MemoryStream(new byte[256]));

    // ML007: closures capturing and retaining state.
    for (var i = 0; i < 2_000; i++)
    {
        var captured = tranche * 1000 + i;
        retained.Add(new Func<int>(() => captured));
    }
}

Grow(0);

Console.WriteLine("READY " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
Console.Out.Flush();

var tranche = 1;
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.Equals(line, "grow", StringComparison.Ordinal))
    {
        Grow(tranche++);
        Console.WriteLine("GROWN");
        Console.Out.Flush();
    }
    else if (string.Equals(line, "exit", StringComparison.Ordinal))
    {
        break;
    }
}

// Keep everything alive to the very end so the heap still holds it when sampled.
GC.KeepAlive(retained);
GC.KeepAlive(duplicates);
GC.KeepAlive(streams);
```

- [ ] **Step 3: Reference it from the integration project**

Add to the `ItemGroup` containing `ProjectReference` entries in `tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj`:

```xml
    <ProjectReference Include="..\MemoryLens.Mcp.LeakyApp\MemoryLens.Mcp.LeakyApp.csproj"
                      ReferenceOutputAssembly="false"
                      OutputItemType="Content"
                      CopyToOutputDirectory="PreserveNewest" />
```

`ReferenceOutputAssembly="false"` matters: the test project must not link against the fixture's types, only ensure it is built and copied next to the tests.

- [ ] **Step 4: Register in the solution**

In `memorylens-mcp.slnx`, add to the `/tests/` folder alongside the existing entries:

```xml
    <Project Path="tests/MemoryLens.Mcp.LeakyApp/MemoryLens.Mcp.LeakyApp.csproj" />
```

- [ ] **Step 5: Write a smoke test for the fixture itself**

`tests/MemoryLens.Mcp.IntegrationTests/LeakyAppFixtureTests.cs`:

```csharp
using System.Diagnostics;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// The fixture is load-bearing for HeapCollectorTests and PipelineTests. If it
/// stops starting or stops answering 'grow', those failures would look like
/// collector bugs. This pins the fixture itself.
/// </summary>
public class LeakyAppFixtureTests
{
    [Fact(Timeout = 60_000)]
    public async Task LeakyApp_AnnouncesReadyAndRespondsToGrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var dll = Path.Combine(AppContext.BaseDirectory, "MemoryLens.Mcp.LeakyApp.dll");
        Assert.True(File.Exists(dll), $"Fixture not found at {dll}");

        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)!;
        try
        {
            var ready = await proc.StandardOutput.ReadLineAsync(ct);
            Assert.NotNull(ready);
            Assert.StartsWith("READY ", ready, StringComparison.Ordinal);
            Assert.True(int.TryParse(ready!["READY ".Length..], out var pid) && pid > 0);

            await proc.StandardInput.WriteLineAsync("grow".AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);

            var grown = await proc.StandardOutput.ReadLineAsync(ct);
            Assert.Equal("GROWN", grown);

            await proc.StandardInput.WriteLineAsync("exit".AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
    }
}
```

- [ ] **Step 6: Run and verify**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release`
Expected: `Passed!`, one more test than before. Record the actual total.

If the fixture DLL is not found, the `ProjectReference` in Step 3 is wrong — check `CopyToOutputDirectory` is present.

- [ ] **Step 7: Commit**

```bash
git add tests/MemoryLens.Mcp.LeakyApp tests/MemoryLens.Mcp.IntegrationTests memorylens-mcp.slnx
git commit -m "test: add the LeakyApp fixture

HeapCollector cannot be unit-tested; it needs a real process with a real
diagnostic endpoint. This is that process, and it retains objects the ML rules
are written to detect so the pipeline test can assert specific rule ids rather
than a bare count.

Driven over stdin rather than timers, so growth is deterministic and no test
depends on wall-clock ordering. The smoke test pins the fixture itself, so a
broken fixture does not later read as a collector bug."
```

---

### Task 3: HeapCollector

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/HeapCollector.cs`
- Create: `tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs`
- Modify: `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`

**Interfaces:**
- Consumes: `TypeClassifier.IsLikelyDisposable(string)` / `IsLikelyFinalizable(string)` from Task 1; the `LeakyApp` fixture from Task 2.
- Produces: `public interface IHeapCollector { Task<SnapshotData> CollectAsync(int pid, CancellationToken ct); }` and `public sealed class HeapCollector : IHeapCollector` with `HeapCollector(TimeSpan? timeout = null)`. Also `public sealed class HeapCollectionException : Exception`. Tasks 4 and 6 consume these.

**Background — read this before writing code.** Two earlier probes hung on this exact component. The cause was **not** the API; it was the buffer size. `dotnet-gcdump` requests **1024 MB**, and with the default buffer events do not stream live — they appear only after `session.Stop()`, which makes live completion detection impossible and looks like a deadlock. Use `circularBufferMB: 1024`.

- [ ] **Step 1: Add the package references**

Add to the existing `ItemGroup` of `PackageReference` entries in `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`:

```xml
    <PackageReference Include="Microsoft.Diagnostics.NETCore.Client" Version="0.2.628101" />
    <PackageReference Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="3.1.16" />
```

Run: `dotnet restore`
Expected: succeeds. If either version has been delisted, take the latest stable of the same major and note the version you used in your report.

- [ ] **Step 2: Write HeapCollector**

`src/MemoryLens.Mcp/Profiler/HeapCollector.cs`:

```csharp
using System.Diagnostics.Tracing;
using MemoryLens.Mcp.Analysis;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace MemoryLens.Mcp.Profiler;

public interface IHeapCollector
{
    Task<SnapshotData> CollectAsync(int pid, CancellationToken ct);
}

/// <summary>Raised when a heap collection cannot produce a usable snapshot.</summary>
public sealed class HeapCollectionException : Exception
{
    public HeapCollectionException(string message) : base(message) { }
    public HeapCollectionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Collects a per-type heap summary from a live .NET process over EventPipe.
/// No external tool, no text parsing.
/// </summary>
public sealed class HeapCollector(TimeSpan? timeout = null) : IHeapCollector
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    // With the default buffer, events do not stream live and surface only after
    // the session stops, which makes completion detection impossible.
    // dotnet-gcdump requests 1024MB for exactly this reason.
    private const int CircularBufferMb = 1024;

    public async Task<SnapshotData> CollectAsync(int pid, CancellationToken ct)
    {
        var providers = new[]
        {
            new EventPipeProvider(
                "Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose,
                (long)ClrTraceEventParser.Keywords.GCHeapSnapshot),
        };

        EventPipeSession session;
        try
        {
            session = new DiagnosticsClient(pid)
                .StartEventPipeSession(providers, requestRundown: false, circularBufferMB: CircularBufferMb);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HeapCollectionException(
                $"Could not attach to process {pid}. It may have exited, may not be a .NET process, " +
                $"or the current user may lack permission to open its diagnostic endpoint.", ex);
        }

        using (session)
        {
            var typeNames = new Dictionary<ulong, string>();
            var counts = new Dictionary<ulong, int>();
            var bytes = new Dictionary<ulong, long>();

            var complete = new TaskCompletionSource();
            long gcNum = -1;
            var sawAnyEvent = false;

            var pump = Task.Run(() =>
            {
                using var source = new EventPipeEventSource(session.EventStream);

                source.Clr.TypeBulkType += e =>
                {
                    sawAnyEvent = true;
                    for (var i = 0; i < e.Count; i++)
                    {
                        var v = e.Values(i);
                        typeNames[v.TypeID] = v.TypeName;
                    }
                };

                source.Clr.GCBulkNode += e =>
                {
                    sawAnyEvent = true;
                    for (var i = 0; i < e.Count; i++)
                    {
                        var n = e.Values(i);
                        counts.TryGetValue(n.TypeID, out var c);
                        counts[n.TypeID] = c + 1;
                        bytes.TryGetValue(n.TypeID, out var b);
                        bytes[n.TypeID] = b + (long)n.Size;
                    }
                };

                // Completion, as dotnet-gcdump does it: remember the induced GC,
                // finish when that same GC stops.
                source.Clr.GCStart += (GCStartTraceData d) =>
                {
                    sawAnyEvent = true;
                    if (gcNum < 0 && d.Depth == 2 && d.Type != GCType.BackgroundGC)
                        gcNum = d.Count;
                };

                source.Clr.GCStop += (GCEndTraceData d) =>
                {
                    if (gcNum >= 0 && d.Count == gcNum)
                        complete.TrySetResult();
                };

                source.Process();
            }, CancellationToken.None);

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(_timeout);

            try
            {
                await complete.Task.WaitAsync(overall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for completion. Fall through: stop the session and
                // see whether what we collected is usable. If it is not, we throw below.
            }

            try { session.Stop(); } catch (Exception) { /* stopping a dead session is not an error */ }

            // Draining is bounded: never let teardown hang the caller.
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None)).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (!sawAnyEvent)
                throw new HeapCollectionException(
                    $"No EventPipe data arrived from process {pid} within {_timeout.TotalSeconds:N0}s. " +
                    $"The process may not be a .NET process, or may have exited during collection.");

            var data = Build(typeNames, counts, bytes);

            // An empty heap is never a real answer. Returning one is how a broken
            // pipeline renders as "no memory issues found" -- see issue #161.
            if (data.Types.Count == 0)
                throw new HeapCollectionException(
                    $"Heap collection from process {pid} produced no objects. Refusing to report an empty snapshot.");

            return data;
        }
    }

    private static SnapshotData Build(
        Dictionary<ulong, string> typeNames,
        Dictionary<ulong, int> counts,
        Dictionary<ulong, long> bytes)
    {
        var types = new List<TypeInfo>(counts.Count);
        long lohBytes = 0;
        var lohCount = 0;

        foreach (var (typeId, count) in counts)
        {
            if (!typeNames.TryGetValue(typeId, out var name) || string.IsNullOrEmpty(name))
                continue;

            var size = bytes.TryGetValue(typeId, out var b) ? b : 0;
            var avg = count > 0 ? size / count : 0;
            var isLoh = avg >= 85_000;

            if (isLoh)
            {
                lohBytes += size;
                lohCount += count;
            }

            types.Add(new TypeInfo
            {
                FullName = name,
                InstanceCount = count,
                TotalBytes = size,
                IsLargeObjectHeap = isLoh,
                ImplementsIDisposable = TypeClassifier.IsLikelyDisposable(name),
                HasFinalizer = TypeClassifier.IsLikelyFinalizable(name),
            });
        }

        return new SnapshotData
        {
            Types = types,
            Heap = new HeapInfo
            {
                TotalBytes = types.Sum(t => t.TotalBytes),
                LargeObjectHeapBytes = lohBytes,
                LargeObjectCount = lohCount,
            },
        };
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/MemoryLens.Mcp/MemoryLens.Mcp.csproj -c Release`
Expected: `Build succeeded`, warnings permitted (this repo does not treat warnings as errors).

If `StartEventPipeSession(providers, requestRundown:, circularBufferMB:)` does not resolve, the installed client package uses the `EventPipeSessionConfiguration` overload instead. In that case use:

```csharp
session = new DiagnosticsClient(pid).StartEventPipeSession(
    new EventPipeSessionConfiguration(providers, circularBufferSizeMB: CircularBufferMb, requestRundown: false));
```

Whichever overload you use, **the buffer must be 1024 MB** — that is the load-bearing detail.

- [ ] **Step 4: Write the collector integration tests**

`tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs`:

```csharp
using System.Diagnostics;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

public class HeapCollectorTests
{
    private static Process StartLeakyApp(out StreamReader stdout, out StreamWriter stdin)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "MemoryLens.Mcp.LeakyApp.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        stdout = p.StandardOutput;
        stdin = p.StandardInput;
        return p;
    }

    [Fact(Timeout = 120_000)]
    public async Task CollectAsync_AgainstLiveProcess_ReturnsNamedTypesWithPlausibleSizes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var ready = await stdout.ReadLineAsync(ct);
            var pid = int.Parse(ready!["READY ".Length..]);

            var data = await new HeapCollector().CollectAsync(pid, ct);

            Assert.NotEmpty(data.Types);
            Assert.All(data.Types, t => Assert.False(string.IsNullOrWhiteSpace(t.FullName)));
            Assert.All(data.Types, t => Assert.True(t.InstanceCount > 0));
            Assert.True(data.Heap.TotalBytes > 0);

            // The fixture retains tens of thousands of strings, so this is not a
            // coincidence -- it proves we collected the fixture's heap specifically.
            var strings = data.Types.Single(t => t.FullName == "System.String");
            Assert.True(strings.InstanceCount > 1000,
                $"expected many retained strings, saw {strings.InstanceCount}");
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }

    [Fact(Timeout = 60_000)]
    public async Task CollectAsync_DeadProcess_ThrowsHeapCollectionException()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        await stdout.ReadLineAsync(ct);
        var pid = app.Id;
        app.Kill(entireProcessTree: true);
        await app.WaitForExitAsync(ct);

        var ex = await Assert.ThrowsAsync<HeapCollectionException>(
            () => new HeapCollector().CollectAsync(pid, ct));

        Assert.Contains(pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task CollectAsync_NonDotNetProcess_ThrowsRatherThanReturningEmpty()
    {
        var ct = TestContext.Current.CancellationToken;

        // PID 0 is never a collectable .NET process on any supported platform.
        await Assert.ThrowsAsync<HeapCollectionException>(
            () => new HeapCollector().CollectAsync(0, ct));
    }

    [Fact(Timeout = 60_000)]
    public async Task CollectAsync_ImpossiblyShortTimeout_ThrowsRatherThanReturningEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var ready = await stdout.ReadLineAsync(ct);
            var pid = int.Parse(ready!["READY ".Length..]);

            // The contract that matters: a collector that could not finish must
            // NEVER hand back an empty snapshot that reads as "no issues found".
            var collector = new HeapCollector(TimeSpan.FromMilliseconds(1));

            await Assert.ThrowsAsync<HeapCollectionException>(() => collector.CollectAsync(pid, ct));
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }
}
```

- [ ] **Step 5: Run them**

Run: `dotnet test tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj -c Release`
Expected: `Passed!`, four more tests than after Task 2.

If `CollectAsync_AgainstLiveProcess_...` times out, check the buffer size is 1024 first — that is the known cause of exactly this symptom. Report the observed behaviour rather than raising the test timeout to hide it.

- [ ] **Step 6: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/HeapCollector.cs src/MemoryLens.Mcp/MemoryLens.Mcp.csproj tests/MemoryLens.Mcp.IntegrationTests/HeapCollectorTests.cs
git commit -m "feat: collect heap data in-process over EventPipe

Replaces shelling out to a tool that was never installed, and parsing a format
nothing emits. Completion follows dotnet-gcdump: remember the induced GC from
GCStart with depth 2, finish when that same GC stops.

The 1024MB circular buffer is load-bearing, not a tuning choice. With the
default buffer, events do not stream live and appear only after the session
stops, which makes completion detection impossible -- two spikes hung on
precisely that.

The collector throws rather than returning an empty snapshot. An empty heap is
never a real answer, and returning one is how a broken pipeline rendered as
'no memory issues found' in issue #161."
```

---

### Task 4: Rewire the pipeline

**Files:**
- Create: `src/MemoryLens.Mcp/Analysis/SnapshotReader.cs`
- Modify: `src/MemoryLens.Mcp/Analysis/AnalysisEngine.cs`
- Modify: `src/MemoryLens.Mcp/Tools/SnapshotTool.cs`
- Modify: `src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs`
- Modify: `src/MemoryLens.Mcp/Program.cs`

**Interfaces:**
- Consumes: `IHeapCollector.CollectAsync(int, CancellationToken)` and `HeapCollectionException` (Task 3); `ISnapshotStore.SaveAsync/LoadAsync` and `SnapshotStore.PathFor(string)` (Task 1).
- Produces: `public interface ISnapshotReader { Task<SnapshotData> ReadAsync(string idOrPath, CancellationToken ct); Task<ComparisonData> CompareAsync(string beforeIdOrPath, string afterIdOrPath, CancellationToken ct); }`. Task 5 deletes the old types this replaces.

**Background:** `AnalysisEngine.EnrichContextAsync` is the ONLY consumer of `IDotMemoryAnalyzer`. Swapping that one interface rewires the whole analysis path. `ComputeDeltas` currently lives in `DotMemoryAnalyzer.cs` and is the only delta logic in the product — it **moves** here, it is not rewritten and not deleted.

The dotMemory files stay on disk during this task and simply stop being referenced. Task 5 deletes them. This keeps "new wiring" and "deletion" separately reviewable.

- [ ] **Step 1: Create SnapshotReader, moving ComputeDeltas across**

Open `src/MemoryLens.Mcp/Analysis/DotMemoryAnalyzer.cs` and copy `ComputeDeltas` verbatim into the new file.

`src/MemoryLens.Mcp/Analysis/SnapshotReader.cs`:

```csharp
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Analysis;

public interface ISnapshotReader
{
    Task<SnapshotData> ReadAsync(string idOrPath, CancellationToken ct);
    Task<ComparisonData> CompareAsync(string beforeIdOrPath, string afterIdOrPath, CancellationToken ct);
}

/// <summary>
/// Reads persisted snapshots and computes deltas between them. Replaces the old
/// analyzer, which shelled out to an external tool and parsed its text output.
/// </summary>
public sealed class SnapshotReader(ISnapshotStore store) : ISnapshotReader
{
    public Task<SnapshotData> ReadAsync(string idOrPath, CancellationToken ct) =>
        store.LoadAsync(idOrPath, ct);

    public async Task<ComparisonData> CompareAsync(
        string beforeIdOrPath, string afterIdOrPath, CancellationToken ct)
    {
        var before = await store.LoadAsync(beforeIdOrPath, ct).ConfigureAwait(false);
        var after = await store.LoadAsync(afterIdOrPath, ct).ConfigureAwait(false);

        return new ComparisonData
        {
            Before = before,
            After = after,
            Deltas = ComputeDeltas(before, after),
        };
    }

    private static List<TypeDelta> ComputeDeltas(SnapshotData before, SnapshotData after)
    {
        var beforeTypes = before.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        var afterTypes = after.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);

        var allTypeNames = beforeTypes.Keys.Union(afterTypes.Keys, StringComparer.Ordinal);
        var deltas = new List<TypeDelta>();

        foreach (var typeName in allTypeNames)
        {
            beforeTypes.TryGetValue(typeName, out var beforeInfo);
            afterTypes.TryGetValue(typeName, out var afterInfo);

            var delta = new TypeDelta
            {
                FullName = typeName,
                InstancesBefore = beforeInfo?.InstanceCount ?? 0,
                InstancesAfter = afterInfo?.InstanceCount ?? 0,
                BytesBefore = beforeInfo?.TotalBytes ?? 0,
                BytesAfter = afterInfo?.TotalBytes ?? 0,
            };

            if (delta.InstanceDelta != 0 || delta.BytesDelta != 0)
                deltas.Add(delta);
        }

        return deltas.OrderByDescending(d => d.BytesDelta).ToList();
    }
}
```

- [ ] **Step 2: Point AnalysisEngine at the new interface**

In `src/MemoryLens.Mcp/Analysis/AnalysisEngine.cs`, change the field and constructor. Before:

```csharp
    private readonly IDotMemoryAnalyzer? _analyzer;

    public AnalysisEngine(MemoryLensConfig config, IDotMemoryAnalyzer? analyzer = null)
    {
        _config = config;
        _analyzer = analyzer;
        RegisterBuiltInRules();
    }
```

After:

```csharp
    private readonly ISnapshotReader? _snapshots;

    public AnalysisEngine(MemoryLensConfig config, ISnapshotReader? snapshots = null)
    {
        _config = config;
        _snapshots = snapshots;
        RegisterBuiltInRules();
    }
```

Keep the optional parameter — existing rule tests construct `AnalysisEngine` with config only. Then inside `EnrichContextAsync` change the null check `if (_analyzer is null)` to `if (_snapshots is null)`, and the two call sites:

- `_analyzer.CompareSnapshotsAsync(context.BeforePath, context.AfterPath, ct)` → `_snapshots.CompareAsync(context.BeforePath, context.AfterPath, ct)`
- `_analyzer.AnalyzeSnapshotAsync(context.SnapshotPath, ct)` → `_snapshots.ReadAsync(context.SnapshotPath, ct)`

Leave the surrounding `context with { ... }` assignments, the null check, and the ordering exactly as they are.

- [ ] **Step 3: Rewire SnapshotTool**

Replace the body of `src/MemoryLens.Mcp/Tools/SnapshotTool.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class SnapshotTool(IHeapCollector collector, ISnapshotStore store, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Takes a memory snapshot of a running .NET process. " +
        "Provide a pid. Returns a snapshot id to pass to analyze.")]
    public async Task<string> snapshot(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name to snapshot")] string? processName = null,
        [Description("Command to launch and snapshot")] string? command = null,
        [Description("Seconds to wait before taking snapshot")] int? durationSeconds = null,
        CancellationToken ct = default)
    {
        if (pid is null)
            return Fail("A process id is required. Use list_processes to find one.");

        if (processName is not null && processFilter.IsExcluded(processName, ""))
            return Fail($"Process '{processName}' is excluded from profiling.");

        if (durationSeconds is > 0)
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds.Value), ct).ConfigureAwait(false);

        try
        {
            var data = await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false);
            var id = await store.SaveAsync(data, ct).ConfigureAwait(false);

            return Serialize(new SnapshotResult(
                true, id, (store as SnapshotStore)?.PathFor(id), null));
        }
        catch (HeapCollectionException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static string Fail(string error) =>
        Serialize(new SnapshotResult(false, null, null, error));

    private static string Serialize(SnapshotResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
```

**Note on `command`:** launching a process and then attaching is a real capability, but it introduces a start-up race that needs its own design and test. It is explicitly **out of scope for Part 1** — the parameter is retained on the signature so existing callers do not break, and passing it without a `pid` returns the "A process id is required" error rather than silently doing nothing. Record this in your report.

- [ ] **Step 4: Rewire CompareSnapshotsTool**

The existing return contract is `ComparisonResult` (`src/MemoryLens.Mcp/Profiler/ComparisonResult.cs`), which is unchanged:

```csharp
public record ComparisonResult(bool Success, string? SnapshotId, string? BeforePath, string? AfterPath, int SnapshotCount, string? Error);
```

Replace the body of `src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class CompareSnapshotsTool(IHeapCollector collector, ISnapshotStore store)
{
    [McpServerTool, Description(
        "Takes two memory snapshots of a .NET process with a delay between them " +
        "for comparison. Useful for detecting memory leaks by comparing before/after state. " +
        "Provide either a pid, processName, or command to profile.")]
    public async Task<string> compare_snapshots(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name to snapshot")] string? processName = null,
        [Description("Command to launch and snapshot")] string? command = null,
        [Description("Seconds to wait between before and after snapshots (default: 10)")] int? delaySeconds = null,
        CancellationToken ct = default)
    {
        if (pid is null)
            return Serialize(new ComparisonResult(false, null, null, null, 0,
                "A process id is required. Use list_processes to find one."));

        try
        {
            var beforeId = await store.SaveAsync(
                await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds ?? 10), ct).ConfigureAwait(false);

            var afterId = await store.SaveAsync(
                await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            var paths = store as SnapshotStore;
            return Serialize(new ComparisonResult(
                true, afterId, paths?.PathFor(beforeId), paths?.PathFor(afterId), 2, null));
        }
        catch (HeapCollectionException ex)
        {
            return Serialize(new ComparisonResult(false, null, null, null, 0, ex.Message));
        }
    }

    private static string Serialize(ComparisonResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
```

Every parameter name and description is unchanged, so the MCP tool schema does not change. `processName` and `command` are accepted but unused, exactly as in `SnapshotTool` — see the note in Step 3.

- [ ] **Step 5: Rewire DI in Program.cs**

In `src/MemoryLens.Mcp/Program.cs`, delete these registrations:

```csharp
builder.Services.AddHttpClient<DotMemoryAutoInstaller>();
builder.Services.AddSingleton<IDotMemoryAutoInstaller>(...);
builder.Services.AddSingleton<DotMemoryToolManager>(...);
builder.Services.AddSingleton<SnapshotManager>();
builder.Services.AddSingleton<IDotMemoryAnalyzer, DotMemoryAnalyzer>();
```

Add:

```csharp
builder.Services.AddSingleton<IHeapCollector>(_ => new HeapCollector());
builder.Services.AddSingleton<ISnapshotStore>(_ => new SnapshotStore());
builder.Services.AddSingleton<ISnapshotReader, SnapshotReader>();
```

Leave `IProcessRunner`, `ProcessFilter`, `IDotNetProcessLister`, `MemoryLensConfig`, `AnalysisEngine`, and the `AddMcpServer()` block untouched.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build -c Release`
Expected: `Build succeeded`. The old dotMemory files still compile; they are simply no longer registered.

Run: `dotnet test -c Release`
Expected: **failures are likely here** — `DotMemoryAnalyzerTests` and `AnalysisEngineTests` may reference `IDotMemoryAnalyzer`. Update those tests to use `ISnapshotReader`; do NOT delete them (Task 5 handles deletions). Record what you changed.

- [ ] **Step 7: Commit**

```bash
git add src/MemoryLens.Mcp tests/MemoryLens.Mcp.Tests
git commit -m "feat: rewire the analysis pipeline to the in-process collector

AnalysisEngine's only external dependency was IDotMemoryAnalyzer, so swapping it
for ISnapshotReader rewires the whole analysis path in one seam. ComputeDeltas
moves across unchanged -- it is the only delta logic in the product.

snapshot now collects in-process and persists JSON; analyze reads it back. The
dotMemory files remain on disk but are no longer referenced, so this commit is
reviewable as new wiring and the next one as pure deletion.

The command parameter on snapshot is retained on the signature but not
implemented: attaching to a process you just launched is a start-up race that
needs its own design. It now returns a clear error instead of doing nothing."
```

---

### Task 5: Delete the dotMemory path

**Files:**
- Delete: `src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs`, `DotMemoryToolManager.cs`, `IDotMemoryAutoInstaller.cs`, `SnapshotManager.cs`
- Delete: `src/MemoryLens.Mcp/Analysis/DotMemoryAnalyzer.cs`, `IDotMemoryAnalyzer.cs`, `GcDumpReportParser.cs`
- Delete: `src/MemoryLens.Mcp/Tools/EnsureDotMemoryTool.cs`
- Delete: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs`, `DotMemoryToolManagerTests.cs`, `FakeDotMemoryAutoInstaller.cs`, `FakeDotMemoryToolManager.cs`
- Delete: `tests/MemoryLens.Mcp.Tests/Analysis/GcDumpReportParserTests.cs`, `Analysis/DotMemoryAnalyzerTests.cs`
- Delete: `tests/MemoryLens.Mcp.IntegrationTests/ExecuteBitTests.cs`, `ExecChainTests.cs`
- Delete: `tests/MemoryLens.Mcp.TestSupport/PackageFixtureBuilder.cs`
- Modify: `src/MemoryLens.Mcp/Properties/AssemblyInfo.cs` (if `InternalsVisibleTo` is now unused by the integration project)
- Modify: both test csproj `--minimum-expected-tests` floors

**Interfaces:**
- Consumes: nothing. Everything here is now unreferenced after Task 4.
- Produces: nothing. Pure deletion.

**Background:** `ExecuteBitTests` and `ExecChainTests` were added on 2026-08-22 as the #118 regression guards. They guard `DotMemoryAutoInstaller`, which is being deleted. **This is intentional and is recorded in the spec** — deleting the code deletes the bug class, which is strictly stronger than guarding it. Do not preserve them.

- [ ] **Step 1: Delete the product files**

```bash
git rm src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs \
       src/MemoryLens.Mcp/Profiler/DotMemoryToolManager.cs \
       src/MemoryLens.Mcp/Profiler/IDotMemoryAutoInstaller.cs \
       src/MemoryLens.Mcp/Profiler/SnapshotManager.cs \
       src/MemoryLens.Mcp/Analysis/DotMemoryAnalyzer.cs \
       src/MemoryLens.Mcp/Analysis/IDotMemoryAnalyzer.cs \
       src/MemoryLens.Mcp/Analysis/GcDumpReportParser.cs \
       src/MemoryLens.Mcp/Tools/EnsureDotMemoryTool.cs
```

- [ ] **Step 2: Delete the tests that guard them**

```bash
git rm tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs \
       tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryToolManagerTests.cs \
       tests/MemoryLens.Mcp.Tests/Profiler/FakeDotMemoryAutoInstaller.cs \
       tests/MemoryLens.Mcp.Tests/Profiler/FakeDotMemoryToolManager.cs \
       tests/MemoryLens.Mcp.Tests/Analysis/GcDumpReportParserTests.cs \
       tests/MemoryLens.Mcp.IntegrationTests/ExecuteBitTests.cs \
       tests/MemoryLens.Mcp.IntegrationTests/ExecChainTests.cs \
       tests/MemoryLens.Mcp.TestSupport/PackageFixtureBuilder.cs
```

Also delete `tests/MemoryLens.Mcp.Tests/Analysis/DotMemoryAnalyzerTests.cs` **only if** its cases are entirely about the deleted analyzer. If any case is really about `ComputeDeltas`, move it to a new `SnapshotReaderTests.cs` first — that logic survives and should keep its coverage. Record which you did.

- [ ] **Step 3: Build and find the fallout**

Run: `dotnet build -c Release`

Expected: errors from anything still referencing the deleted types — most likely `ToolIntegrationTests.cs` in the unit project, which constructs `EnsureDotMemoryTool`. Delete the `ensure_dotmemory` cases there; keep the rest of the file.

Repeat build-and-fix until it is green. Report every file you touched.

- [ ] **Step 4: Check InternalsVisibleTo**

Run: `grep -rn "MakeToolsExecutable\|NeedsExecuteBit" tests/ --include=*.cs`
Expected: no matches (those tests are gone).

If there are none, remove the now-unused line from `src/MemoryLens.Mcp/Properties/AssemblyInfo.cs`:

```csharp
[assembly: InternalsVisibleTo("MemoryLens.Mcp.IntegrationTests")]
```

Leave the `MemoryLens.Mcp.Tests` line — the unit project still uses internals.

- [ ] **Step 5: Recalibrate both floors**

Run: `dotnet test -c Release`

Read the ACTUAL per-project totals from the output. Then set each project's `TestingPlatformCommandLineArguments` to that project's actual count **minus 2**, matching the existing convention:

- `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj`
- `tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj`

Do not guess these numbers — this task deletes tests and Task 6 adds them, so only a real run knows the count.

- [ ] **Step 6: Verify the floors fire**

Temporarily set the integration floor to `999`, run that project, and confirm FAIL with exit code 9 and the shape `error: 1, failed: 0`. Set it back and confirm PASS. An unverified guard is what this whole effort exists to stop shipping.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat!: remove the dotMemory path and the ensure_dotmemory tool

BREAKING CHANGE: the ensure_dotmemory MCP tool is removed. Six tools become
five. Clients referencing it by name will get an unknown-tool error.

dotMemory Console cannot analyze anything -- it has no report command, and .dmw
workspaces need the paid standalone GUI. So snapshot has always written an
artifact analyze could never read. With collection now in-process, the installer,
the tool manager, the report parser and the analyzer are all unreferenced.

This also deletes the #118 execute-bit guards added earlier today. That is
deliberate: deleting DotMemoryAutoInstaller deletes the bug class those tests
guarded, which is stronger than guarding it."
```

---

### Task 6: The pipeline test that would have caught #161

**Files:**
- Create: `tests/MemoryLens.Mcp.IntegrationTests/PipelineTests.cs`
- Modify: `tests/MemoryLens.Mcp.IntegrationTests/MemoryLens.Mcp.IntegrationTests.csproj` (final floor)

**Interfaces:**
- Consumes: `HeapCollector` (Task 3), `SnapshotStore` / `SnapshotReader` (Tasks 1, 4), the `LeakyApp` fixture and its stdin protocol (Task 2), and the existing `AnalysisEngine` / `SnapshotAnalysisContext`.
- Produces: nothing.

**Background:** This is the test whose absence let #161 ship. Every existing rule test feeds hand-written text to a parser; none has ever seen data from a real heap. These run the whole chain — real process, real EventPipe collection, real persistence, real rule evaluation.

- [ ] **Step 1: Write the pipeline tests**

`tests/MemoryLens.Mcp.IntegrationTests/PipelineTests.cs`:

```csharp
using System.Diagnostics;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// The whole chain on real data: live process -> EventPipe collection ->
/// persistence -> rule evaluation. Every existing rule test feeds hand-written
/// text to a parser; none has ever seen a real heap. That gap is what let
/// issue #161 ship.
/// </summary>
public class PipelineTests
{
    private static Process StartLeakyApp(out StreamReader stdout, out StreamWriter stdin)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "MemoryLens.Mcp.LeakyApp.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        stdout = p.StandardOutput;
        stdin = p.StandardInput;
        return p;
    }

    [Fact(Timeout = 120_000)]
    public async Task CollectStoreAnalyze_ProducesFindingsFromARealHeap()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var pid = int.Parse(( await stdout.ReadLineAsync(ct))!["READY ".Length..]);

            var data = await new HeapCollector().CollectAsync(pid, ct);
            var store = new SnapshotStore(dir.Path);
            var id = await store.SaveAsync(data, ct);

            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
            var findings = await engine.AnalyzeAsync(
                new SnapshotAnalysisContext(id, id, null, null, false, null), ct);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.RuleId)));

            // Every finding must name a rule the engine actually has -- catches a
            // finding fabricated from an empty or malformed snapshot.
            var knownIds = engine.GetActiveRules().Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            Assert.All(findings, f => Assert.Contains(f.RuleId, knownIds));
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }

    [Fact(Timeout = 180_000)]
    public async Task CompareAcrossGrow_ShowsRealGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        using var app = StartLeakyApp(out var stdout, out var stdin);
        try
        {
            var pid = int.Parse((await stdout.ReadLineAsync(ct))!["READY ".Length..]);

            var collector = new HeapCollector();
            var store = new SnapshotStore(dir.Path);

            var beforeId = await store.SaveAsync(await collector.CollectAsync(pid, ct), ct);

            await stdin.WriteLineAsync("grow".AsMemory(), ct);
            await stdin.FlushAsync(ct);
            Assert.Equal("GROWN", await stdout.ReadLineAsync(ct));

            var afterId = await store.SaveAsync(await collector.CollectAsync(pid, ct), ct);

            var comparison = await new SnapshotReader(store).CompareAsync(beforeId, afterId, ct);

            Assert.NotEmpty(comparison.Deltas);

            // The fixture only ever adds; the largest delta must be growth.
            Assert.True(comparison.Deltas[0].BytesDelta > 0,
                $"expected growth, largest delta was {comparison.Deltas[0].BytesDelta}");

            var strings = comparison.Deltas.SingleOrDefault(d => d.FullName == "System.String");
            Assert.NotNull(strings);
            Assert.True(strings!.InstanceDelta > 0,
                $"expected more strings after grow, delta was {strings.InstanceDelta}");
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }

    [Fact(Timeout = 60_000)]
    public async Task Analyze_OnAnEmptySnapshot_FindsNothingAndSaysSoHonestly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();

        // A snapshot with no types can only come from a broken pipeline. The
        // collector refuses to produce one; if a hand-written one is analyzed,
        // it must yield no findings rather than fabricating any.
        var store = new SnapshotStore(dir.Path);
        var id = await store.SaveAsync(new SnapshotData(), ct);

        var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
        var findings = await engine.AnalyzeAsync(
            new SnapshotAnalysisContext(id, id, null, null, false, null), ct);

        Assert.Empty(findings);
    }
}
```

- [ ] **Step 2: Run and record the count**

Run: `dotnet test -c Release`
Expected: `Passed!`. Record the ACTUAL per-project totals.

If `CollectStoreAnalyze_...` finds no findings, do NOT weaken the assertion. It means the fixture is not retaining enough for any rule to fire — increase the tranche sizes in `LeakyApp/Program.cs` and report that you did.

- [ ] **Step 3: Set the final floor**

Set the integration project's `--minimum-expected-tests` to its actual count minus 2.

- [ ] **Step 4: Verify the full suite one more time**

Run: `dotnet test -c Release`
Expected: `Passed!`, both projects, floors active.

- [ ] **Step 5: Commit**

```bash
git add tests/MemoryLens.Mcp.IntegrationTests
git commit -m "test: prove the whole pipeline works on a real heap

Live process -> EventPipe collection -> persistence -> rule evaluation, with
findings asserted against the engine's own rule ids so a finding fabricated from
an empty snapshot cannot pass.

This is the test whose absence let #161 ship: every existing rule test feeds
hand-written text to a parser, so the parser and the rules were thoroughly
tested against a format nothing in the pipeline ever emitted."
```

---

## Done when

- `dotnet test -c Release` passes with both projects and both floors active.
- `analyze` returns real findings from a real heap — proven by `PipelineTests`.
- `HeapCollector` throws rather than returning an empty snapshot, proven in two tests.
- No file under `src/` references dotMemory, `gcdump`, or `SnapshotManager`.
- `grep -rn "ensure_dotmemory" src/` returns nothing.

## Handoff to Part 2

Part 2 covers what this part deliberately leaves alone:

- **README, `server.json`, and the csproj `<Description>`** still claim the product wraps JetBrains dotMemory. All three are now false.
- **`docs/docker.md`** still documents mounting a volume so a runtime profiler download survives `--rm`. There is no download any more; the image is self-contained.
- **The `command` parameter on `snapshot`** is accepted but not implemented (Task 4, Step 3). Launch-then-attach is a start-up race needing its own design and test.
- **The nightly `e2e.yml` tier** from the superseded Phase 3 — Docker, npm shim, pack assertion — is now much smaller and no longer licence-gated.
