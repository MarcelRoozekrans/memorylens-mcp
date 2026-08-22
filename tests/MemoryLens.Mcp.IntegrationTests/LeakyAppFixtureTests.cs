using System.Diagnostics;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// The fixture is load-bearing for HeapCollectorTests and PipelineTests. If it
/// stops starting or stops answering 'grow', those failures would look like
/// collector bugs. This pins the fixture itself.
/// </summary>
public class LeakyAppFixtureTests
{
    [Fact(Timeout = 60_000)]
    public async Task LeakyApp_AnnouncesReadyAndRespondsToGrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var dll = Path.Combine(AppContext.BaseDirectory, "MemoryLens.Mcp.LeakyApp.dll");
        Assert.True(File.Exists(dll), $"Fixture not found at {dll}");

        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)!;
        try
        {
            var ready = await proc.StandardOutput.ReadLineAsync(ct);
            Assert.NotNull(ready);
            Assert.StartsWith("READY ", ready, StringComparison.Ordinal);
            Assert.True(int.TryParse(ready!["READY ".Length..], out var pid) && pid > 0);

            await proc.StandardInput.WriteLineAsync("grow".AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);

            var grown = await proc.StandardOutput.ReadLineAsync(ct);
            Assert.Equal("GROWN", grown);

            await proc.StandardInput.WriteLineAsync("exit".AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
    }
}
