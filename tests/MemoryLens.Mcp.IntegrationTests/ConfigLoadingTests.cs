using System.Text.Json;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// Program.cs reads .memorylens.json from the current working directory. Config
/// that silently fails to load is invisible -- the server starts fine and quietly
/// uses defaults -- so this round-trips a real file on a real filesystem.
/// </summary>
public class ConfigLoadingTests
{
    [Fact]
    public void LoadFromPath_MissingFile_ReturnsDefaultsRatherThanThrowing()
    {
        using var dir = new TempDir();
        var missing = Path.Combine(dir.Path, ".memorylens.json");

        var config = ConfigLoader.LoadFromPath(missing);

        // The server must start without a config file present.
        Assert.NotNull(config);
    }

    [Fact]
    public void LoadFromPath_RealFileOnDisk_IsActuallyApplied()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, ".memorylens.json");

        File.WriteAllText(path, """
            {
              "rules": {
                "ML001": { "severity": "info", "enabled": false }
              },
              "ignore": ["System.String"]
            }
            """);

        var config = ConfigLoader.LoadFromPath(path);

        Assert.True(config.Rules.ContainsKey("ML001"));

        // Enabled defaults to true, so false here can ONLY come from the file.
        Assert.False(config.Rules["ML001"].Enabled);
        Assert.Equal("info", config.Rules["ML001"].Severity);
        Assert.Contains("System.String", config.Ignore);
    }

    [Fact]
    public void LoadFromPath_DefaultsDifferFromTheFixture_SoTheTestCannotBeVacuous()
    {
        // Guards the test above: if defaults ever changed to match the fixture,
        // LoadFromPath_RealFileOnDisk_IsActuallyApplied would pass without reading
        // anything. This pins the defaults it relies on.
        var defaults = ConfigLoader.Parse("{}");

        Assert.Empty(defaults.Rules);
        Assert.Empty(defaults.Ignore);
        Assert.True(new RuleOverride().Enabled);
    }

    /// <summary>
    /// The seam the three tests above cannot reach: Program.cs resolves the config as
    /// Path.Combine(Directory.GetCurrentDirectory(), ".memorylens.json"). Calling
    /// LoadFromPath with an explicit path proves the parser works but says nothing about
    /// whether a real server, started in a real directory, ever finds that file. This
    /// starts one and observes the effect through the wire: a disabled ML001 must vanish
    /// from get_rules, because AnalysisEngine.GetActiveRules filters on RuleOverride.Enabled.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task ServerReadsMemorylensJsonFromItsWorkingDirectory()
    {
        var ct = TestContext.Current.CancellationToken;

        using var dir = new TempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir.Path, ".memorylens.json"),
            """{"rules":{"ML001":{"enabled":false}}}""",
            ct);

        await using var client = McpStdioClient.StartServer(
            TimeSpan.FromSeconds(45), workingDirectory: dir.Path);
        await client.InitializeAsync(ct);

        var result = await client.CallToolAsync("get_rules", new { }, ct);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));

        using var payload = JsonDocument.Parse(text!);
        var ids = payload.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Select(r => r.GetProperty("Id").GetString()!)
            .ToList();

        // The only way ML001 can be missing is if the server read the file we wrote.
        Assert.DoesNotContain("ML001", ids, StringComparer.Ordinal);

        // ...and the rest must still be there, so this cannot pass on an empty payload.
        Assert.Contains("ML002", ids, StringComparer.Ordinal);
    }
}
