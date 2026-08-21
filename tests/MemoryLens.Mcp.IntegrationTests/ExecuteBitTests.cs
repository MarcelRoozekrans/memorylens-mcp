using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// Regression guard for #118. ZipFile.ExtractToDirectory discards Unix permission
/// bits, so everything lands 0644; the installer must restore the execute bit on
/// every file the OS has to exec, decided by leading bytes rather than by name.
/// </summary>
public class ExecuteBitTests
{
    private static bool IsExecutable(string path) =>
        File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute);

    [Fact]
    public void Extraction_LandsEverythingNonExecutable()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are not meaningful here.

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        // This is the bug's premise: without intervention, nothing is executable.
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ShebangEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_SetsBitOnEveryExecFormat()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);

        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ShebangEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.NestedShebangEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.MachOEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_LeavesNonExecFormatsAlone()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);

        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ManagedEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_IsIdempotent()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);
        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path); // cache-hit path

        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.NestedShebangEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }

    [Fact]
    public void NeedsExecuteBit_ClassifiesByLeadingBytes_NotByName()
    {
        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        // Runs on every OS: this is pure byte inspection, no file modes involved.
        Assert.True(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
        Assert.True(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.MachOEntry)));
        Assert.False(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.ManagedEntry)));
        Assert.False(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }
}
