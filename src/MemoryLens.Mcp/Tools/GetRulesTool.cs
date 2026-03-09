using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class GetRulesTool(AnalysisEngine analysisEngine)
{
    [McpServerTool, Description(
        "Lists all active analysis rules with their ID, title, severity, and category. " +
        "Rules can be configured via .memorylens.json in the project root.")]
    public string get_rules()
    {
        var rules = analysisEngine.GetActiveRules()
            .Select(r => new { r.Id, r.Title, r.Severity, r.Category })
            .ToList();

        return JsonSerializer.Serialize(new { rules, count = rules.Count }, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
