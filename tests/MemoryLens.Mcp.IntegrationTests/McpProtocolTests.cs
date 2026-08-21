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
        "ensure_dotmemory",
        "get_rules",
        "list_processes",
        "snapshot",
    ];

    [Fact]
    public async Task Server_StartsAndCompletesTheHandshake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer();

        var result = await client.InitializeAsync(ct);

        Assert.Equal("MemoryLens.Mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.TryGetProperty("capabilities", out var caps));
        Assert.True(caps.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task ToolsList_ExposesExactlyTheExpectedTools()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer();
        await client.InitializeAsync(ct);

        var names = await client.ListToolNamesAsync(ct);

        // Exact equality on purpose: this catches a tool silently dropped by the
        // reflection-based discovery AND an unintended new one.
        Assert.Equal(ExpectedTools, names);
    }

    [Fact]
    public async Task GetRules_RoundTripsOverTheWire()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = McpStdioClient.StartServer();
        await client.InitializeAsync(ct);

        var result = await client.CallToolAsync("get_rules", new { }, ct);

        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));

        // The payload must be real JSON carrying real rules, not an error string.
        using var payload = JsonDocument.Parse(text!);
        var ids = payload.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Select(r => r.GetProperty("Id").GetString())
            .ToList();

        Assert.Contains("ML001", ids);
        Assert.Equal(10, ids.Count);
    }
}
