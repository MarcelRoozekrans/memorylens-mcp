using System.Text.Json;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// Nothing in the unit suite touches Program.cs, the DI container, or the MCP
/// protocol. Tools are discovered by reflection via WithToolsFromAssembly(), so a
/// renamed class or changed attribute drops a tool from the manifest with no
/// compile error. These tests spawn the real server and speak real JSON-RPC.
/// </summary>
public class McpProtocolTests
{
    /// <summary>Every tool the server is contracted to expose.</summary>
    private static readonly string[] ExpectedTools =
    [
        "analyze",
        "compare_snapshots",
        "get_rules",
        "list_processes",
        "snapshot",
    ];

    /// <summary>Every rule the server is contracted to expose via get_rules.</summary>
    private static readonly string[] ExpectedRuleIds =
    [
        "ML001",
        "ML002",
        "ML003",
        "ML004",
        "ML005",
        "ML006",
        "ML007",
        "ML008",
        "ML009",
        "ML010",
    ];

    [Fact(Timeout = 60_000)]
    public async Task Server_StartsAndCompletesTheHandshake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer(TimeSpan.FromSeconds(45));

        var result = await client.InitializeAsync(ct);

        Assert.Equal("MemoryLens.Mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.TryGetProperty("capabilities", out var caps));
        Assert.True(caps.TryGetProperty("tools", out _));
    }

    [Fact(Timeout = 60_000)]
    public async Task ToolsList_ExposesExactlyTheExpectedTools()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer(TimeSpan.FromSeconds(45));
        await client.InitializeAsync(ct);

        var names = await client.ListToolNamesAsync(ct);

        // Exact equality on purpose: this catches a tool silently dropped by the
        // reflection-based discovery AND an unintended new one.
        Assert.Equal(ExpectedTools, names);
    }

    [Fact(Timeout = 60_000)]
    public async Task GetRules_RoundTripsOverTheWire()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer(TimeSpan.FromSeconds(45));
        await client.InitializeAsync(ct);

        var result = await client.CallToolAsync("get_rules", new { }, ct);

        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));

        // The payload must be real JSON carrying real rules, not an error string.
        using var payload = JsonDocument.Parse(text!);
        var ids = payload.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Select(r => r.GetProperty("Id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Exact equality against a hardcoded set, mirroring ExpectedTools above.
        // Cross-checking against the payload's own "count" would be vacuous:
        // GetRulesTool serialises count from the very list it serialises, so that
        // assertion holds for any payload that parses at all.
        Assert.Equal(ExpectedRuleIds, ids, StringComparer.Ordinal);
    }
}
