using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.Rules;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the full analysis pipeline:
/// SnapshotStore → SnapshotReader → AnalysisEngine → Rules → Findings
/// </summary>
public class AnalysisPipelineTests
{
    /// <summary>
    /// Simulates a realistic heap from a .NET web app with memory issues. Hand-built
    /// SnapshotData rather than parsed gcdump text, so these tests do not depend on
    /// GcDumpReportParser (Task 5 deletes it).
    /// </summary>
    private static SnapshotData RealisticSnapshot() => new()
    {
        Types =
        [
            new TypeInfo { FullName = "System.String", InstanceCount = 55000, TotalBytes = 6600000 },
            new TypeInfo
            {
                FullName = "System.EventHandler",
                InstanceCount = 200,
                TotalBytes = 500000,
                ImplementsIDisposable = true,
            },
            new TypeInfo
            {
                FullName = "System.IO.FileStream",
                InstanceCount = 150,
                TotalBytes = 2000000,
                ImplementsIDisposable = true,
                HasFinalizer = true,
            },
            new TypeInfo { FullName = "System.Object[]", InstanceCount = 5000, TotalBytes = 3000000 },
            new TypeInfo
            {
                FullName = "System.Collections.Generic.List`1[[System.String]]",
                InstanceCount = 120,
                TotalBytes = 300000,
            },
            new TypeInfo
            {
                FullName = "MyApp.Services.UserService+<>c__DisplayClass5_0",
                InstanceCount = 80,
                TotalBytes = 250000,
            },
            new TypeInfo
            {
                FullName = "System.Byte[]",
                InstanceCount = 10,
                TotalBytes = 1200000,
                IsLargeObjectHeap = true,
            },
            new TypeInfo { FullName = "System.Int32", InstanceCount = 15, TotalBytes = 100000 },
            new TypeInfo { FullName = "MyApp.NativeWrapper", InstanceCount = 5, TotalBytes = 500 },
            new TypeInfo
            {
                FullName = "System.Threading.Timer",
                InstanceCount = 30,
                TotalBytes = 50000,
                ImplementsIDisposable = true,
                HasFinalizer = true,
            },
        ],
        Heap = new HeapInfo { TotalBytes = 13900500, LargeObjectHeapBytes = 1200000, LargeObjectCount = 10 },
    };

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-pipeline-tests-" + Guid.NewGuid().ToString("N"));

    private static async Task<(SnapshotStore Store, string Id)> SaveAsync(
        string root, SnapshotData data, CancellationToken ct)
    {
        var store = new SnapshotStore(root);
        var id = await store.SaveAsync(data, ct).ConfigureAwait(false);
        return (store, id);
    }

    [Fact]
    public async Task FullPipeline_SingleSnapshot_DetectsMultipleIssues()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, RealisticSnapshot(), TestContext.Current.CancellationToken);
            var config = new MemoryLensConfig();
            var engine = new AnalysisEngine(config, new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-abc123", id, null, null, false, null);

            // Act
            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            // Assert: multiple rules should fire
            Assert.NotEmpty(findings);

            // ML001: 200 EventHandler instances > threshold of 50
            Assert.Contains(findings, f => f.RuleId == "ML001");

            // ML003: FileStream is disposable with 150 instances
            Assert.Contains(findings, f => f.RuleId == "ML003");

            // ML006: System.String has 55000 instances
            Assert.Contains(findings, f => f.RuleId == "ML006");

            // ML008: System.Object[] has 5000 instances > 1000 threshold
            Assert.Contains(findings, f => f.RuleId == "ML008");

            // ML010: 55000 strings at 6.6MB, avg ~120 bytes → interning opportunity
            Assert.Contains(findings, f => f.RuleId == "ML010");

            // ML007: Closure type with 80 instances > 50 threshold, 250KB > 100KB threshold
            Assert.Contains(findings, f => f.RuleId == "ML007");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FullPipeline_SingleSnapshot_FindingsHaveCorrectStructure()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, RealisticSnapshot(), TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-abc123", id, null, null, false, null);

            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            // Every finding should have required fields populated
            foreach (var finding in findings)
            {
                Assert.False(string.IsNullOrEmpty(finding.RuleId));
                Assert.False(string.IsNullOrEmpty(finding.Severity));
                Assert.False(string.IsNullOrEmpty(finding.Category));
                Assert.False(string.IsNullOrEmpty(finding.Title));
                Assert.False(string.IsNullOrEmpty(finding.Description));
                Assert.NotNull(finding.Evidence);
                Assert.False(string.IsNullOrEmpty(finding.Evidence.Type));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FullPipeline_Comparison_DetectsGrowth()
    {
        var before = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 10000, TotalBytes = 1200000 },
                new TypeInfo
                {
                    FullName = "System.EventHandler",
                    InstanceCount = 20,
                    TotalBytes = 50000,
                    ImplementsIDisposable = true,
                },
                new TypeInfo
                {
                    FullName = "System.Collections.Generic.Dictionary`2[[System.String,System.Object]]",
                    InstanceCount = 50,
                    TotalBytes = 100000,
                },
            ],
            Heap = new HeapInfo { TotalBytes = 1350000 },
        };

        var after = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 35000, TotalBytes = 4200000 },
                new TypeInfo
                {
                    FullName = "System.EventHandler",
                    InstanceCount = 150,
                    TotalBytes = 375000,
                    ImplementsIDisposable = true,
                },
                new TypeInfo
                {
                    FullName = "System.Collections.Generic.Dictionary`2[[System.String,System.Object]]",
                    InstanceCount = 120,
                    TotalBytes = 500000,
                },
            ],
            Heap = new HeapInfo { TotalBytes = 5075000 },
        };

        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var beforeId = await store.SaveAsync(before, TestContext.Current.CancellationToken);
            var afterId = await store.SaveAsync(after, TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "cmp-abc123", null, beforeId, afterId, true, null);

            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            Assert.NotEmpty(findings);

            // ML001: EventHandler grew from 20 to 150 (7.5x)
            Assert.Contains(findings, f => f.RuleId == "ML001");

            // ML002: Dictionary grew from 50 to 120 (2.4x > 1.5x threshold)
            Assert.Contains(findings, f => f.RuleId == "ML002");

            // ML006: String count jumped by 25000 in comparison mode
            Assert.Contains(findings, f => f.RuleId == "ML006");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FullPipeline_DisabledRules_AreSkipped()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, RealisticSnapshot(), TestContext.Current.CancellationToken);
            var config = new MemoryLensConfig
            {
                Rules = new Dictionary<string, RuleOverride>
                {
                    ["ML001"] = new() { Enabled = false },
                    ["ML006"] = new() { Enabled = false },
                    ["ML010"] = new() { Enabled = false },
                }
            };
            var engine = new AnalysisEngine(config, new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-abc123", id, null, null, false, null);

            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(findings, f => f.RuleId == "ML001");
            Assert.DoesNotContain(findings, f => f.RuleId == "ML006");
            Assert.DoesNotContain(findings, f => f.RuleId == "ML010");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FullPipeline_SeverityOverride_IsApplied()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, RealisticSnapshot(), TestContext.Current.CancellationToken);
            var config = new MemoryLensConfig
            {
                Rules = new Dictionary<string, RuleOverride>
                {
                    ["ML001"] = new() { Severity = "low" },
                }
            };
            var engine = new AnalysisEngine(config, new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-abc123", id, null, null, false, null);

            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            var ml001 = findings.Where(f => f.RuleId == "ML001").ToList();
            Assert.NotEmpty(ml001);
            Assert.All(ml001, f => Assert.Equal("low", f.Severity));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// The old analyzer swallowed a failing external tool and returned an empty
    /// snapshot, so rules silently produced no findings. The new pipeline never does
    /// that: a snapshot id/path that cannot be loaded is a real failure and surfaces
    /// as an exception -- naming the missing id -- instead of being papered over.
    /// </summary>
    [Fact]
    public async Task FullPipeline_UnreadableSnapshot_Throws()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-abc123", "no-such-snapshot", null, null, false, null);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.AnalyzeAsync(context, TestContext.Current.CancellationToken));

            Assert.Contains("no-such-snapshot", ex.Message, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FullPipeline_CleanApp_NoFindings()
    {
        // Small, healthy app — nothing should trigger
        var clean = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 100, TotalBytes = 10000 },
                new TypeInfo { FullName = "System.Object[]", InstanceCount = 5, TotalBytes = 500 },
                new TypeInfo { FullName = "System.Int32", InstanceCount = 2, TotalBytes = 200 },
            ],
            Heap = new HeapInfo { TotalBytes = 10700 },
        };

        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, clean, TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));

            var context = new SnapshotAnalysisContext(
                "snap-clean", id, null, null, false, null);

            var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

            Assert.Empty(findings);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
