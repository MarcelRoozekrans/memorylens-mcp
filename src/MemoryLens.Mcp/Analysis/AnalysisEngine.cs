using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;

namespace MemoryLens.Mcp.Analysis;

public class AnalysisEngine
{
    private readonly MemoryLensConfig _config;
    private readonly List<IRule> _rules = [];

    public AnalysisEngine(MemoryLensConfig config)
    {
        _config = config;
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
        var findings = new List<RuleFinding>();

        foreach (var rule in GetActiveRules())
        {
            var ruleFindings = await rule.EvaluateAsync(context, ct);

            foreach (var finding in ruleFindings)
            {
                var severity = GetEffectiveSeverity(finding.RuleId, finding.Severity);
                findings.Add(finding with { Severity = severity });
            }
        }

        return findings;
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
