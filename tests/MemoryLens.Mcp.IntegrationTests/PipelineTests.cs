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
            var pid = int.Parse((await stdout.ReadLineAsync(ct))!["READY ".Length..]);

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
