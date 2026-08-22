using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;

namespace MemoryLens.Mcp.Analysis;

public class AnalysisEngine
{
    private readonly MemoryLensConfig _config;
    private readonly ISnapshotReader? _snapshots;
    private readonly List<IRule> _rules = [];

    public AnalysisEngine(MemoryLensConfig config, ISnapshotReader? snapshots = null)
    {
        _config = config;
        _snapshots = snapshots;
        RegisterBuiltInRules();
    }

    private void RegisterBuiltInRules()
    {
        _rules.Add(new ML001_EventHandlerLeak());
        _rules.Add(new ML002_StaticCollectionGrowing());
        _rules.Add(new ML003_DisposableNotDisposed());
        _rules.Add(new ML004_LargeObjectHeapFragmentation());
        _rules.Add(new ML005_ObjectRetainedTooLong());
        _rules.Add(new ML006_ExcessiveAllocations());
        _rules.Add(new ML007_ClosureRetainingReferences());
        _rules.Add(new ML008_ArrayResizingWithoutCapacity());
        _rules.Add(new ML009_FinalizerWithoutDispose());
        _rules.Add(new ML010_StringInterningOpportunity());
    }

    public IReadOnlyList<IRule> GetActiveRules()
    {
        return _rules
            .Where(r => IsRuleEnabled(r.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<RuleFinding>> AnalyzeAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var enrichedContext = await EnrichContextAsync(context, ct).ConfigureAwait(false);
        var findings = new List<RuleFinding>();

        foreach (var rule in GetActiveRules())
        {
            var ruleFindings = await rule.EvaluateAsync(enrichedContext, ct).ConfigureAwait(false);

            foreach (var finding in ruleFindings)
            {
                var severity = GetEffectiveSeverity(finding.RuleId, finding.Severity);
                findings.Add(finding with { Severity = severity });
            }
        }

        return findings;
    }

    private async Task<SnapshotAnalysisContext> EnrichContextAsync(
        SnapshotAnalysisContext context, CancellationToken ct)
    {
        if (_snapshots is null)
            return context;

        if (context.IsComparison && context.BeforePath is not null && context.AfterPath is not null)
        {
            var comparison = await _snapshots.CompareAsync(
                context.BeforePath, context.AfterPath, ct).ConfigureAwait(false);

            return context with
            {
                Data = comparison.After,
                Comparison = comparison,
            };
        }

        // The documented workflow is "snapshot returns an id, pass that id to analyze".
        // The id IS a resolvable locator -- SnapshotStore.LoadAsync accepts either an id
        // or a full path -- so when no explicit path is given, fall back to the id.
        //
        // Fixed here rather than in AnalyzeTool on purpose: enrichment is the single
        // place a locator turns into data, so every caller of the engine gets it, not
        // just the one tool. Before this, SnapshotId was declared but never read by any
        // production code, Data stayed null, every rule took its `Data is null`
        // early-out, and the happy path answered "no memory issues found" on a leaking
        // heap -- the exact silent-wrong-answer shape this pipeline exists to eliminate.
        // Empty is treated as absent, not as a path. A calling model routinely emits ""
        // for an optional string, and a null-only fallback would let that skip the read
        // entirely -- reinstating the silent "no memory issues found" this code exists
        // to prevent, for an input that previously threw.
        var locator = string.IsNullOrEmpty(context.SnapshotPath)
            ? context.SnapshotId
            : context.SnapshotPath;

        if (!string.IsNullOrEmpty(locator))
        {
            var data = await _snapshots.ReadAsync(locator, ct).ConfigureAwait(false);
            return context with { Data = data };
        }

        return context;
    }

    private bool IsRuleEnabled(string ruleId)
    {
        if (_config.Rules.TryGetValue(ruleId, out var ruleOverride))
            return ruleOverride.Enabled;

        return true;
    }

    private string GetEffectiveSeverity(string ruleId, string defaultSeverity)
    {
        if (_config.Rules.TryGetValue(ruleId, out var ruleOverride) && ruleOverride.Severity != null)
            return ruleOverride.Severity;

        return defaultSeverity;
    }
}
