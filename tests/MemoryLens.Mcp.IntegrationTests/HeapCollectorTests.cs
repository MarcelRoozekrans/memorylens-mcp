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
}
