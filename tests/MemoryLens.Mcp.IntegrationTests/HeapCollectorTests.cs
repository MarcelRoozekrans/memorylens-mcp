using System.Diagnostics;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

public class HeapCollectorTests
{
    // Test-only. The product default is 30s so a large real heap is never
    // truncated; the fixture's heap completes in ~200ms, so CI need not wait
    // anywhere near that long. Do not push this value into the product.
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

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

            var data = await new HeapCollector(TestTimeout).CollectAsync(pid, ct);

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

    /// <summary>
    /// Large Object Heap classification is computed inside <c>HeapCollector.Build</c>,
    /// which is private static -- so the only honest coverage is end-to-end. The rules
    /// that used to pin the 85KB boundary lived in a parser that no longer exists, which
    /// left the classifier with no test at all.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task CollectAsync_ClassifiesLargeObjectHeapTypes_AndDiscriminates()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var ready = await stdout.ReadLineAsync(ct);
            var pid = int.Parse(ready!["READY ".Length..]);

            var data = await new HeapCollector(TestTimeout).CollectAsync(pid, ct);

            // The fixture retains 16 double[20_000] buffers, ~160KB each -- comfortably
            // over the 85KB threshold -- and allocates System.Double[] nowhere else. The
            // collector aggregates by type NAME, so a type that also had small instances
            // would have its average dragged back under the threshold and prove nothing.
            // Exact name, not a prefix: the List<double[]> holding them has a
            // System.Double[][] backing store, which is a handful of small objects.
            var doubleArrays = data.Types
                .Where(t => string.Equals(t.FullName, "System.Double[]", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                doubleArrays.Count == 1,
                $"expected exactly one System.Double[] row from the fixture's large buffers, saw " +
                $"{doubleArrays.Count}. Double-ish rows present: " +
                $"[{string.Join(", ", data.Types.Select(t => t.FullName)
                    .Where(n => n.Contains("Double", StringComparison.Ordinal)))}]");

            var large = doubleArrays[0];
            var average = large.TotalBytes / large.InstanceCount;

            Assert.True(
                large.IsLargeObjectHeap,
                $"{large.FullName} averages {average} bytes across {large.InstanceCount} " +
                $"instances ({large.TotalBytes} bytes total) and must be classified as LOH");

            Assert.True(
                data.Heap.LargeObjectHeapBytes > 0,
                $"a heap holding {large.InstanceCount} LOH-sized buffers reported " +
                $"LargeObjectHeapBytes = {data.Heap.LargeObjectHeapBytes}");

            Assert.True(
                data.Heap.LargeObjectCount > 0,
                $"a heap holding {large.InstanceCount} LOH-sized buffers reported " +
                $"LargeObjectCount = {data.Heap.LargeObjectCount}");

            // The classifier must discriminate, not flag everything. Strings in this
            // fixture are tens of bytes each, three orders of magnitude under the line.
            var strings = data.Types.Single(t => t.FullName == "System.String");
            Assert.False(
                strings.IsLargeObjectHeap,
                $"System.String averages {strings.TotalBytes / strings.InstanceCount} bytes " +
                $"and must not be classified as LOH");

            // And the roll-up must be a subset of the heap, not the whole of it.
            Assert.True(
                data.Heap.LargeObjectHeapBytes < data.Heap.TotalBytes,
                $"LOH bytes {data.Heap.LargeObjectHeapBytes} should be a strict subset of " +
                $"total {data.Heap.TotalBytes}");
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }

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
            () => new HeapCollector(TestTimeout).CollectAsync(pid, ct));

        Assert.Contains(pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task CollectAsync_NonDotNetProcess_ThrowsRatherThanReturningEmpty()
    {
        var ct = TestContext.Current.CancellationToken;

        // PID 0 is never a collectable .NET process on any supported platform.
        await Assert.ThrowsAsync<HeapCollectionException>(
            () => new HeapCollector(TestTimeout).CollectAsync(0, ct));
    }

    [Fact(Timeout = 60_000)]
    public async Task CollectAsync_ImpossiblyShortTimeout_NeverReturnsEmptySnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = StartLeakyApp(out var stdout, out _);
        try
        {
            var ready = await stdout.ReadLineAsync(ct);
            var pid = int.Parse(ready!["READY ".Length..]);

            // The contract that matters: a collector that could not wait out the
            // induced GC must NEVER hand back an empty snapshot that reads as
            // "no issues found". It may legitimately do either of two things:
            // throw, or salvage a real snapshot from what the session already
            // buffered. Both are honest; an empty result is not.
            //
            // In practice it salvages. The heap dump is queued into the 1024MB
            // buffer when the session starts, so session.Stop() flushes the whole
            // thing even when the wait timed out after 1ms -- so this test must
            // pin the invariant, not the branch.
            var collector = new HeapCollector(TimeSpan.FromMilliseconds(1));

            SnapshotData data;
            try
            {
                data = await collector.CollectAsync(pid, ct);
            }
            catch (HeapCollectionException)
            {
                return; // Refused to answer rather than answering emptily. Correct.
            }

            Assert.NotEmpty(data.Types);
            Assert.All(data.Types, t => Assert.False(string.IsNullOrWhiteSpace(t.FullName)));
            Assert.All(data.Types, t => Assert.True(t.InstanceCount > 0));
            Assert.True(data.Heap.TotalBytes > 0);
        }
        finally { if (!app.HasExited) app.Kill(entireProcessTree: true); }
    }

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
}
