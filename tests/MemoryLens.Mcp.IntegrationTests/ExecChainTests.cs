using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// #118 died on the SECOND hop of the exec chain: the entry point was chmodded,
/// the script it exec'd was not. File modes alone do not prove the chain runs,
/// so this drives a real two-hop exec through the real ProcessRunner.
/// </summary>
public class ExecChainTests
{
    [Fact]
    public async Task TwoHopExecChain_RunsAfterExecuteBitsAreRestored()
    {
        if (OperatingSystem.IsWindows())
            return; // No execute-bit concept; the chain is a Unix-only failure mode.

        using var dir = new TempDir();

        // Second hop, written first so the first hop can exec it.
        var secondPath = Path.Combine(dir.Path, "second.sh");
        await File.WriteAllTextAsync(secondPath, "#!/bin/sh\necho SECOND_HOP_OK\n",
            TestContext.Current.CancellationToken);

        var firstPath = Path.Combine(dir.Path, "first.sh");
        await File.WriteAllTextAsync(firstPath, $"#!/bin/sh\nexec \"{secondPath}\"\n",
            TestContext.Current.CancellationToken);

        // Mirror what extraction does: strip the execute bit from everything.
        File.SetUnixFileMode(firstPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(secondPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(firstPath, "", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SECOND_HOP_OK", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondHopWithoutExecuteBit_FailsTheWay118Did()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();

        var secondPath = Path.Combine(dir.Path, "second.sh");
        await File.WriteAllTextAsync(secondPath, "#!/bin/sh\necho SECOND_HOP_OK\n",
            TestContext.Current.CancellationToken);

        var firstPath = Path.Combine(dir.Path, "first.sh");
        await File.WriteAllTextAsync(firstPath, $"#!/bin/sh\nexec \"{secondPath}\"\n",
            TestContext.Current.CancellationToken);

        // Chmod ONLY the entry point -- precisely the broken behaviour #118 fixed.
        File.SetUnixFileMode(firstPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(secondPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(firstPath, "", TestContext.Current.CancellationToken);

        // The chain must NOT succeed -- this proves the test above is not vacuous.
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("SECOND_HOP_OK", result.Output, StringComparison.Ordinal);
    }
}
