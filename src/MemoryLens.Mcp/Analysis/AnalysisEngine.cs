using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;

namespace MemoryLens.Mcp.Analysis;

public class AnalysisEngine
{
    private readonly MemoryLensConfig _config;
    private readonly IDotMemoryAnalyzer? _analyzer;
    private readonly List<IRule> _rules = [];

    public AnalysisEngine(MemoryLensConfig config, IDotMemoryAnalyzer? analyzer = null)
    {
        _config = config;
        _analyzer = analyzer;
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
        if (_analyzer is null)
            return context;

        if (context.IsComparison && context.BeforePath is not null && context.AfterPath is not null)
        {
            var comparison = await _analyzer.CompareSnapshotsAsync(
                context.BeforePath, context.AfterPath, ct).ConfigureAwait(false);

            return context with
            {
                Data = comparison.After,
                Comparison = comparison,
            };
        }

        if (context.SnapshotPath is not null)
        {
            var data = await _analyzer.AnalyzeSnapshotAsync(context.SnapshotPath, ct).ConfigureAwait(false);
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
