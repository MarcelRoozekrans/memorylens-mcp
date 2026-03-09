# MemoryLens MCP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a .NET MCP server that wraps `dotnet-dotmemory` for on-demand memory profiling with built-in rules and concrete code fix suggestions.

**Architecture:** Thin .NET MCP server using `ModelContextProtocol` SDK. Shells out to `dotnet-dotmemory` CLI for profiling. Parses structured output and runs a rule engine that produces code diffs. Packaged as a standalone Claude Code marketplace plugin.

**Tech Stack:** .NET 9, ModelContextProtocol NuGet, dotnet-dotmemory global tool, GitVersion, conventional commits

---

### Task 1: Project Scaffolding

**Files:**
- Create: `memorylens-mcp.sln`
- Create: `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`
- Create: `src/MemoryLens.Mcp/Program.cs`
- Create: `.gitignore`
- Create: `.editorconfig`

**Step 1: Create solution and project**

```bash
dotnet new sln -n memorylens-mcp
mkdir -p src/MemoryLens.Mcp
dotnet new console -n MemoryLens.Mcp -o src/MemoryLens.Mcp --framework net9.0
dotnet sln add src/MemoryLens.Mcp/MemoryLens.Mcp.csproj
```

**Step 2: Add NuGet dependencies**

```bash
cd src/MemoryLens.Mcp
dotnet add package ModelContextProtocol --prerelease
dotnet add package Microsoft.Extensions.Hosting
```

**Step 3: Create minimal MCP server in `Program.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

**Step 4: Create `.gitignore`**

Use standard .NET gitignore (bin/, obj/, *.user, .vs/, etc.).

**Step 5: Create `.editorconfig`**

Standard C# editorconfig with `indent_style = space`, `indent_size = 4`.

**Step 6: Verify it builds**

```bash
dotnet build
```

Expected: Build succeeded.

**Step 7: Commit**

```bash
git add .gitignore .editorconfig memorylens-mcp.sln src/
git commit -m "feat: scaffold .NET MCP server project"
```

---

### Task 2: GitVersion, Conventional Commits & Changelog

**Files:**
- Create: `GitVersion.yml`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Modify: `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`

**Step 1: Install GitVersion as a dotnet tool**

```bash
dotnet new tool-manifest
dotnet tool install GitVersion.Tool
```

**Step 2: Create `GitVersion.yml`**

```yaml
mode: MainLine
major-version-bump-message: '^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\([\w\s-,/\\]*\))?(!:|:.*\n\n((.+\n)+\n)?BREAKING CHANGE:\s.+)'
minor-version-bump-message: '^(feat)(\([\w\s-,/\\]*\))?:'
patch-version-bump-message: '^(fix|perf)(\([\w\s-,/\\]*\))?:'
```

**Step 3: Configure csproj for GitVersion**

Add to `MemoryLens.Mcp.csproj` PropertyGroup:

```xml
<GenerateAssemblyInfo>true</GenerateAssemblyInfo>
```

**Step 4: Create CI workflow `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - uses: gittools/actions/gitversion/setup@v3.2
        with:
          versionSpec: '6.x'
      - uses: gittools/actions/gitversion/execute@v3.2
        id: gitversion
      - run: dotnet build -c Release /p:Version=${{ steps.gitversion.outputs.semVer }}
      - run: dotnet test -c Release --no-build
```

**Step 5: Create release workflow `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - uses: gittools/actions/gitversion/setup@v3.2
        with:
          versionSpec: '6.x'
      - uses: gittools/actions/gitversion/execute@v3.2
        id: gitversion
      - run: dotnet build -c Release /p:Version=${{ steps.gitversion.outputs.semVer }}
      - name: Generate changelog
        id: changelog
        uses: requarks/changelog-action@v1
        with:
          token: ${{ secrets.GITHUB_TOKEN }}
          tag: ${{ github.ref_name }}
      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          body: ${{ steps.changelog.outputs.changes }}
          token: ${{ secrets.GITHUB_TOKEN }}
```

**Step 6: Verify GitVersion runs**

```bash
dotnet gitversion
```

Expected: JSON output with SemVer version.

**Step 7: Commit**

```bash
git add GitVersion.yml .config/ .github/
git commit -m "ci: add GitVersion, conventional commits, CI/CD workflows"
```

---

### Task 3: Tool Manager — `ensure_dotmemory`

**Files:**
- Create: `src/MemoryLens.Mcp/Tools/EnsureDotMemoryTool.cs`
- Create: `src/MemoryLens.Mcp/Profiler/DotMemoryToolManager.cs`
- Create: `tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryToolManagerTests.cs`

**Step 1: Create test project**

```bash
mkdir -p tests/MemoryLens.Mcp.Tests
dotnet new xunit -n MemoryLens.Mcp.Tests -o tests/MemoryLens.Mcp.Tests --framework net9.0
dotnet sln add tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj
dotnet add tests/MemoryLens.Mcp.Tests reference src/MemoryLens.Mcp
dotnet add tests/MemoryLens.Mcp.Tests package NSubstitute
```

**Step 2: Write failing test for `DotMemoryToolManager`**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryToolManagerTests.cs
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class DotMemoryToolManagerTests
{
    [Fact]
    public async Task EnsureInstalled_ReturnsStatus_WhenToolExists()
    {
        var manager = new DotMemoryToolManager(new FakeProcessRunner(
            exitCode: 0,
            output: "dotnet-dotmemory  2024.3.0  dotnet-dotmemory"));

        var result = await manager.EnsureInstalledAsync();

        Assert.True(result.IsInstalled);
        Assert.Contains("2024.3.0", result.Version);
    }

    [Fact]
    public async Task EnsureInstalled_InstallsTool_WhenNotFound()
    {
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        runner.SetNextResult(exitCode: 0, output: "Tool 'dotnet-dotmemory' was successfully installed.");
        var manager = new DotMemoryToolManager(runner);

        var result = await manager.EnsureInstalledAsync();

        Assert.True(result.IsInstalled);
    }
}
```

**Step 3: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL — types don't exist yet.

**Step 4: Implement `IProcessRunner` abstraction**

```csharp
// src/MemoryLens.Mcp/Profiler/IProcessRunner.cs
namespace MemoryLens.Mcp.Profiler;

public record ProcessResult(int ExitCode, string Output, string Error);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments,
        CancellationToken ct = default);
}
```

```csharp
// src/MemoryLens.Mcp/Profiler/ProcessRunner.cs
using System.Diagnostics;

namespace MemoryLens.Mcp.Profiler;

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments,
        CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, output, error);
    }
}
```

**Step 5: Implement `DotMemoryToolManager`**

```csharp
// src/MemoryLens.Mcp/Profiler/DotMemoryToolManager.cs
namespace MemoryLens.Mcp.Profiler;

public record ToolStatus(bool IsInstalled, string? Version, string Message);

public class DotMemoryToolManager(IProcessRunner processRunner)
{
    public async Task<ToolStatus> EnsureInstalledAsync(CancellationToken ct = default)
    {
        // Check if already installed
        var listResult = await processRunner.RunAsync(
            "dotnet", "tool list -g", ct);

        if (listResult.ExitCode == 0 && listResult.Output.Contains("dotnet-dotmemory"))
        {
            var version = ParseVersion(listResult.Output);
            // Try update
            await processRunner.RunAsync(
                "dotnet", "tool update -g dotnet-dotmemory", ct);
            return new ToolStatus(true, version, $"dotnet-dotmemory {version} is installed.");
        }

        // Install
        var installResult = await processRunner.RunAsync(
            "dotnet", "tool install -g dotnet-dotmemory", ct);

        if (installResult.ExitCode != 0)
            return new ToolStatus(false, null,
                $"Failed to install dotnet-dotmemory: {installResult.Error}");

        return new ToolStatus(true, null, "dotnet-dotmemory installed successfully.");
    }

    private static string? ParseVersion(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("dotnet-dotmemory", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1] : null;
        }
        return null;
    }
}
```

**Step 6: Implement `FakeProcessRunner` for tests**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/FakeProcessRunner.cs
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public FakeProcessRunner(int exitCode, string output)
    {
        _results.Enqueue(new ProcessResult(exitCode, output, ""));
    }

    public void SetNextResult(int exitCode, string output)
    {
        _results.Enqueue(new ProcessResult(exitCode, output, ""));
    }

    public Task<ProcessResult> RunAsync(string fileName, string arguments,
        CancellationToken ct = default)
    {
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new ProcessResult(0, "", "");
        return Task.FromResult(result);
    }
}
```

**Step 7: Implement MCP tool**

```csharp
// src/MemoryLens.Mcp/Tools/EnsureDotMemoryTool.cs
using System.ComponentModel;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class EnsureDotMemoryTool(DotMemoryToolManager toolManager)
{
    [McpServerTool, Description(
        "Ensures dotnet-dotmemory is installed and up-to-date. " +
        "Call this before any profiling operation.")]
    public async Task<string> ensure_dotmemory(CancellationToken ct)
    {
        var status = await toolManager.EnsureInstalledAsync(ct);
        return status.Message;
    }
}
```

**Step 8: Register services in `Program.cs`**

Add DI registrations before `builder.Build()`:

```csharp
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<DotMemoryToolManager>();
```

**Step 9: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 10: Commit**

```bash
git add -A
git commit -m "feat: add ensure_dotmemory tool with tool manager"
```

---

### Task 4: Process Safety — `list_processes`

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/ProcessFilter.cs`
- Create: `src/MemoryLens.Mcp/Tools/ListProcessesTool.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/ProcessFilterTests.cs`

**Step 1: Write failing test for `ProcessFilter`**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/ProcessFilterTests.cs
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class ProcessFilterTests
{
    [Theory]
    [InlineData("devenv", true)]
    [InlineData("rider64", true)]
    [InlineData("Code", true)]
    [InlineData("code-insiders", true)]
    [InlineData("ServiceHub.Host", true)]
    [InlineData("dotnet-dotmemory", true)]
    [InlineData("MyApp", false)]
    [InlineData("WebApi", false)]
    public void IsExcluded_FiltersCorrectly(string processName, bool expectedExcluded)
    {
        var filter = new ProcessFilter();
        Assert.Equal(expectedExcluded, filter.IsExcluded(processName, ""));
    }

    [Fact]
    public void IsExcluded_FiltersRoslynCodegraph_ByCommandLine()
    {
        var filter = new ProcessFilter();
        Assert.True(filter.IsExcluded("dotnet",
            "dotnet run --project roslyn-codegraph-mcp"));
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL

**Step 3: Implement `ProcessFilter`**

```csharp
// src/MemoryLens.Mcp/Profiler/ProcessFilter.cs
namespace MemoryLens.Mcp.Profiler;

public class ProcessFilter
{
    private static readonly string[] ExcludedProcessNames =
    [
        "devenv",
        "rider64",
        "JetBrains.Rider",
        "Code",
        "code-insiders",
        "ServiceHub",
        "dotnet-dotmemory",
        "MemoryLens.Mcp"
    ];

    private static readonly string[] ExcludedCommandLinePatterns =
    [
        "roslyn-codegraph-mcp",
        "memorylens-mcp"
    ];

    public bool IsExcluded(string processName, string commandLine)
    {
        if (ExcludedProcessNames.Any(excluded =>
                processName.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrEmpty(commandLine) &&
            ExcludedCommandLinePatterns.Any(pattern =>
                commandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}
```

**Step 4: Implement `ListProcessesTool`**

```csharp
// src/MemoryLens.Mcp/Tools/ListProcessesTool.cs
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class ListProcessesTool(IProcessRunner processRunner, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Lists running .NET processes suitable for memory profiling. " +
        "Excludes IDE, tooling, and MCP server processes to prevent interference.")]
    public async Task<string> list_processes(
        [Description("Optional filter to match process name")] string? filter = null,
        CancellationToken ct = default)
    {
        var result = await processRunner.RunAsync(
            "dotnet", "dotmemory list-processes", ct);

        if (result.ExitCode != 0)
            return $"Failed to list processes: {result.Error}";

        var processes = ParseProcessList(result.Output)
            .Where(p => !processFilter.IsExcluded(p.Name, p.CommandLine))
            .Where(p => filter == null ||
                        p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(processes, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static IEnumerable<DotNetProcess> ParseProcessList(string output)
    {
        // Parse dotnet-dotmemory list-processes output
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var pid))
            {
                yield return new DotNetProcess(pid, parts[1].Trim(), "");
            }
        }
    }
}

public record DotNetProcess(int Pid, string Name, string CommandLine);
```

**Step 5: Register in DI**

Add to `Program.cs`:

```csharp
builder.Services.AddSingleton<ProcessFilter>();
```

**Step 6: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add list_processes tool with process safety filter"
```

---

### Task 5: Snapshot Capture — `snapshot` Tool

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/SnapshotManager.cs`
- Create: `src/MemoryLens.Mcp/Profiler/SnapshotResult.cs`
- Create: `src/MemoryLens.Mcp/Tools/SnapshotTool.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/SnapshotManagerTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/SnapshotManagerTests.cs
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotManagerTests
{
    [Fact]
    public async Task TakeSnapshot_ByPid_ReturnsSnapshotId()
    {
        var runner = new FakeProcessRunner(0,
            "Snapshot saved to C:\\Snapshots\\snapshot-001.dmw");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(pid: 5678);

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
        Assert.Contains("snapshot", result.SnapshotPath!);
    }

    [Fact]
    public async Task TakeSnapshot_ExcludedProcess_ReturnsError()
    {
        var runner = new FakeProcessRunner(0, "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(processName: "devenv");

        Assert.False(result.Success);
        Assert.Contains("excluded", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL

**Step 3: Implement `SnapshotResult`**

```csharp
// src/MemoryLens.Mcp/Profiler/SnapshotResult.cs
namespace MemoryLens.Mcp.Profiler;

public record SnapshotResult(
    bool Success,
    string? SnapshotId,
    string? SnapshotPath,
    string? Error);
```

**Step 4: Implement `SnapshotManager`**

```csharp
// src/MemoryLens.Mcp/Profiler/SnapshotManager.cs
namespace MemoryLens.Mcp.Profiler;

public class SnapshotManager(IProcessRunner processRunner, ProcessFilter processFilter)
{
    private readonly string _snapshotDir = Path.Combine(
        Path.GetTempPath(), "memorylens-snapshots");

    public async Task<SnapshotResult> TakeSnapshotAsync(
        int? pid = null,
        string? processName = null,
        string? command = null,
        int? durationSeconds = null,
        CancellationToken ct = default)
    {
        // Validate not excluded
        if (processName != null && processFilter.IsExcluded(processName, ""))
            return new SnapshotResult(false, null, null,
                $"Process '{processName}' is excluded — attaching would interfere with your IDE or tooling.");

        Directory.CreateDirectory(_snapshotDir);
        var snapshotId = $"snapshot-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N[..8]}";

        string args;
        if (command != null)
        {
            // Launch under profiling
            args = $"start --save-to-dir=\"{_snapshotDir}\" " +
                   $"--snapshot-name=\"{snapshotId}\" -- {command}";
        }
        else
        {
            var target = pid?.ToString() ?? processName!;
            args = $"get-snapshot {target} --save-to-dir=\"{_snapshotDir}\" " +
                   $"--snapshot-name=\"{snapshotId}\"";
        }

        if (durationSeconds.HasValue)
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds.Value), ct);

        var result = await processRunner.RunAsync("dotnet-dotmemory", args, ct);

        if (result.ExitCode != 0)
            return new SnapshotResult(false, null, null,
                $"dotMemory failed: {result.Error}");

        var snapshotPath = FindSnapshotFile(snapshotId);
        return new SnapshotResult(true, snapshotId, snapshotPath, null);
    }

    private string? FindSnapshotFile(string snapshotId)
    {
        if (!Directory.Exists(_snapshotDir)) return null;
        return Directory.GetFiles(_snapshotDir, $"*{snapshotId}*")
            .FirstOrDefault();
    }
}
```

**Step 5: Implement MCP tool**

```csharp
// src/MemoryLens.Mcp/Tools/SnapshotTool.cs
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class SnapshotTool(SnapshotManager snapshotManager)
{
    [McpServerTool, Description(
        "Takes a single memory snapshot of a .NET process. " +
        "Provide either pid, processName, or command to launch.")]
    public async Task<string> snapshot(
        [Description("Process ID to attach to")] int? pid = null,
        [Description("Process name to attach to")] string? processName = null,
        [Description("Command to launch under profiling")] string? command = null,
        [Description("Seconds to wait before taking snapshot")] int? duration = null,
        CancellationToken ct = default)
    {
        if (pid == null && processName == null && command == null)
            return "Error: provide pid, processName, or command.";

        var result = await snapshotManager.TakeSnapshotAsync(
            pid, processName, command, duration, ct);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
```

**Step 6: Register in DI**

Add to `Program.cs`:

```csharp
builder.Services.AddSingleton<SnapshotManager>();
```

**Step 7: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 8: Commit**

```bash
git add -A
git commit -m "feat: add snapshot tool for single memory snapshots"
```

---

### Task 6: Comparison Snapshots — `compare_snapshots` Tool

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/ComparisonResult.cs`
- Modify: `src/MemoryLens.Mcp/Profiler/SnapshotManager.cs` (add comparison method)
- Create: `src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/SnapshotManagerCompareTests.cs`

**Step 1: Write failing test**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/SnapshotManagerCompareTests.cs
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotManagerCompareTests
{
    [Fact]
    public async Task CompareSnapshots_TakesTwoSnapshots_ReturnsDelta()
    {
        var runner = new FakeProcessRunner(0, "Profiling started for PID 1234");
        runner.SetNextResult(0, "Snapshot 1 saved.");
        runner.SetNextResult(0, "Snapshot 2 saved.");
        var manager = new SnapshotManager(runner, new ProcessFilter());

        var result = await manager.CompareSnapshotsAsync(pid: 1234,
            waitBeforeMs: 0, waitAfterMs: 0);

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
        Assert.Equal(2, result.SnapshotCount);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test
```

Expected: FAIL

**Step 3: Implement `ComparisonResult`**

```csharp
// src/MemoryLens.Mcp/Profiler/ComparisonResult.cs
namespace MemoryLens.Mcp.Profiler;

public record ComparisonResult(
    bool Success,
    string? SnapshotId,
    string? BeforePath,
    string? AfterPath,
    int SnapshotCount,
    string? Error);
```

**Step 4: Add `CompareSnapshotsAsync` to `SnapshotManager`**

```csharp
public async Task<ComparisonResult> CompareSnapshotsAsync(
    int? pid = null,
    string? processName = null,
    string? command = null,
    int waitBeforeMs = 5000,
    int waitAfterMs = 10000,
    CancellationToken ct = default)
{
    if (processName != null && processFilter.IsExcluded(processName, ""))
        return new ComparisonResult(false, null, null, null, 0,
            $"Process '{processName}' is excluded.");

    Directory.CreateDirectory(_snapshotDir);
    var snapshotId = $"compare-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..8]}";

    // Start profiling / attach
    string target;
    if (command != null)
    {
        var startResult = await processRunner.RunAsync(
            "dotnet-dotmemory",
            $"start --save-to-dir=\"{_snapshotDir}\" -- {command}", ct);
        if (startResult.ExitCode != 0)
            return new ComparisonResult(false, null, null, null, 0, startResult.Error);
        target = "all";
    }
    else
    {
        target = (pid?.ToString() ?? processName)!;
    }

    // Wait, then first snapshot
    await Task.Delay(waitBeforeMs, ct);
    var snap1 = await processRunner.RunAsync(
        "dotnet-dotmemory",
        $"get-snapshot {target} --save-to-dir=\"{_snapshotDir}\" " +
        $"--snapshot-name=\"{snapshotId}-before\"", ct);

    // Wait, then second snapshot
    await Task.Delay(waitAfterMs, ct);
    var snap2 = await processRunner.RunAsync(
        "dotnet-dotmemory",
        $"get-snapshot {target} --save-to-dir=\"{_snapshotDir}\" " +
        $"--snapshot-name=\"{snapshotId}-after\"", ct);

    if (snap1.ExitCode != 0 || snap2.ExitCode != 0)
        return new ComparisonResult(false, null, null, null, 0,
            $"Snapshot failed: {snap1.Error} {snap2.Error}");

    return new ComparisonResult(true, snapshotId,
        FindSnapshotFile($"{snapshotId}-before"),
        FindSnapshotFile($"{snapshotId}-after"),
        2, null);
}
```

**Step 5: Implement MCP tool**

```csharp
// src/MemoryLens.Mcp/Tools/CompareSnapshotsTool.cs
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class CompareSnapshotsTool(SnapshotManager snapshotManager)
{
    [McpServerTool, Description(
        "Takes two memory snapshots with a delay between them to compare memory state. " +
        "Useful for detecting leaks and growth patterns.")]
    public async Task<string> compare_snapshots(
        [Description("Process ID to attach to")] int? pid = null,
        [Description("Process name to attach to")] string? processName = null,
        [Description("Command to launch under profiling")] string? command = null,
        [Description("Milliseconds to wait before first snapshot")] int waitBefore = 5000,
        [Description("Milliseconds to wait before second snapshot")] int waitAfter = 10000,
        CancellationToken ct = default)
    {
        if (pid == null && processName == null && command == null)
            return "Error: provide pid, processName, or command.";

        var result = await snapshotManager.CompareSnapshotsAsync(
            pid, processName, command, waitBefore, waitAfter, ct);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
```

**Step 6: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add compare_snapshots tool for memory comparison"
```

---

### Task 7: Rule Engine — Built-in Rules

**Files:**
- Create: `src/MemoryLens.Mcp/Rules/IRule.cs`
- Create: `src/MemoryLens.Mcp/Rules/RuleFinding.cs`
- Create: `src/MemoryLens.Mcp/Rules/CodeSuggestion.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML001_EventHandlerLeak.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML002_StaticCollectionGrowth.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML003_DisposableLeak.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML004_LohFragmentation.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML005_Gen2Retention.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML006_ExcessiveAllocations.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML007_ClosureRetention.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML008_ArrayResizing.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML009_FinalizerWithoutDispose.cs`
- Create: `src/MemoryLens.Mcp/Rules/BuiltIn/ML010_StringInterning.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Rules/BuiltIn/ML001Tests.cs`

**Step 1: Define rule interfaces**

```csharp
// src/MemoryLens.Mcp/Rules/IRule.cs
namespace MemoryLens.Mcp.Rules;

public interface IRule
{
    string Id { get; }
    string Title { get; }
    string Severity { get; }
    string Category { get; }
    Task<IReadOnlyList<RuleFinding>> EvaluateAsync(
        SnapshotAnalysisContext context, CancellationToken ct = default);
}
```

```csharp
// src/MemoryLens.Mcp/Rules/RuleFinding.cs
namespace MemoryLens.Mcp.Rules;

public record RuleFinding(
    string RuleId,
    string Severity,
    string Category,
    string Title,
    string Description,
    RuleEvidence Evidence,
    CodeSuggestion? Suggestion);

public record RuleEvidence(
    string Type,
    long RetainedBytes,
    int InstanceCount,
    string? RetentionPath);
```

```csharp
// src/MemoryLens.Mcp/Rules/CodeSuggestion.cs
namespace MemoryLens.Mcp.Rules;

public record CodeSuggestion(
    string File,
    int Line,
    string Old,
    string New);
```

```csharp
// src/MemoryLens.Mcp/Rules/SnapshotAnalysisContext.cs
namespace MemoryLens.Mcp.Rules;

public record SnapshotAnalysisContext(
    string SnapshotId,
    string? SnapshotPath,
    string? BeforePath,
    string? AfterPath,
    bool IsComparison,
    string? WorkingDirectory);
```

**Step 2: Write failing test for ML001**

```csharp
// tests/MemoryLens.Mcp.Tests/Rules/BuiltIn/ML001Tests.cs
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML001Tests
{
    [Fact]
    public void Rule_HasCorrectMetadata()
    {
        var rule = new ML001_EventHandlerLeak();

        Assert.Equal("ML001", rule.Id);
        Assert.Equal("critical", rule.Severity);
        Assert.Equal("leak", rule.Category);
    }
}
```

**Step 3: Run test to verify it fails**

```bash
dotnet test
```

Expected: FAIL

**Step 4: Implement all 10 built-in rules**

Each rule follows the same pattern. Implement `ML001_EventHandlerLeak` as the reference:

```csharp
// src/MemoryLens.Mcp/Rules/BuiltIn/ML001_EventHandlerLeak.cs
namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML001_EventHandlerLeak : IRule
{
    public string Id => "ML001";
    public string Title => "Event handler leak detected";
    public string Severity => "critical";
    public string Category => "leak";

    public async Task<IReadOnlyList<RuleFinding>> EvaluateAsync(
        SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        // Implementation will parse dotMemory snapshot data
        // looking for growing delegate chains and event subscriptions
        // that are never unsubscribed.
        //
        // For now, return empty — analysis logic depends on
        // dotMemory output format which will be refined during
        // integration testing.
        await Task.CompletedTask;
        return [];
    }
}
```

Implement the remaining 9 rules (ML002–ML010) following the same pattern, each with correct `Id`, `Title`, `Severity`, and `Category` matching the design document.

**Step 5: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add rule engine with 10 built-in memory analysis rules"
```

---

### Task 8: Configuration — `.memorylens.json`

**Files:**
- Create: `src/MemoryLens.Mcp/Config/MemoryLensConfig.cs`
- Create: `src/MemoryLens.Mcp/Config/ConfigLoader.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Config/ConfigLoaderTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/MemoryLens.Mcp.Tests/Config/ConfigLoaderTests.cs
using MemoryLens.Mcp.Config;

namespace MemoryLens.Mcp.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_WithOverrides_MergesCorrectly()
    {
        var json = """
        {
            "rules": {
                "ML004": { "severity": "critical", "threshold": { "minBytes": 52428800 } },
                "ML010": { "enabled": false }
            },
            "ignore": ["*.Tests.*", "*.Benchmarks.*"]
        }
        """;

        var config = ConfigLoader.Parse(json);

        Assert.Equal("critical", config.Rules["ML004"].Severity);
        Assert.False(config.Rules["ML010"].Enabled);
        Assert.Contains("*.Tests.*", config.Ignore);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = ConfigLoader.LoadFromPath("/nonexistent/.memorylens.json");

        Assert.Empty(config.Rules);
        Assert.Empty(config.Ignore);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL

**Step 3: Implement config models**

```csharp
// src/MemoryLens.Mcp/Config/MemoryLensConfig.cs
namespace MemoryLens.Mcp.Config;

public class MemoryLensConfig
{
    public Dictionary<string, RuleOverride> Rules { get; set; } = new();
    public List<string> Ignore { get; set; } = [];
}

public class RuleOverride
{
    public string? Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object>? Threshold { get; set; }
}
```

**Step 4: Implement `ConfigLoader`**

```csharp
// src/MemoryLens.Mcp/Config/ConfigLoader.cs
using System.Text.Json;

namespace MemoryLens.Mcp.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static MemoryLensConfig Parse(string json)
    {
        return JsonSerializer.Deserialize<MemoryLensConfig>(json, Options)
               ?? new MemoryLensConfig();
    }

    public static MemoryLensConfig LoadFromPath(string path)
    {
        if (!File.Exists(path))
            return new MemoryLensConfig();

        var json = File.ReadAllText(path);
        return Parse(json);
    }
}
```

**Step 5: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add .memorylens.json configuration loader"
```

---

### Task 9: Analysis Engine — `analyze` and `get_rules` Tools

**Files:**
- Create: `src/MemoryLens.Mcp/Analysis/AnalysisEngine.cs`
- Create: `src/MemoryLens.Mcp/Tools/AnalyzeTool.cs`
- Create: `src/MemoryLens.Mcp/Tools/GetRulesTool.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Analysis/AnalysisEngineTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/MemoryLens.Mcp.Tests/Analysis/AnalysisEngineTests.cs
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;

namespace MemoryLens.Mcp.Tests.Analysis;

public class AnalysisEngineTests
{
    [Fact]
    public async Task Analyze_RunsAllEnabledRules()
    {
        var config = new MemoryLensConfig();
        var engine = new AnalysisEngine(config);
        var context = new SnapshotAnalysisContext(
            "test-snapshot", null, null, null, false, null);

        var findings = await engine.AnalyzeAsync(context);

        // With default config all 10 rules are enabled
        Assert.NotNull(findings);
    }

    [Fact]
    public async Task Analyze_SkipsDisabledRules()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Enabled = false }
            }
        };
        var engine = new AnalysisEngine(config);
        var context = new SnapshotAnalysisContext(
            "test-snapshot", null, null, null, false, null);

        var findings = await engine.AnalyzeAsync(context);

        Assert.DoesNotContain(findings, f => f.RuleId == "ML001");
    }

    [Fact]
    public void GetActiveRules_RespectsConfig()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML010"] = new() { Enabled = false }
            }
        };
        var engine = new AnalysisEngine(config);

        var rules = engine.GetActiveRules();

        Assert.Equal(9, rules.Count);
        Assert.DoesNotContain(rules, r => r.Id == "ML010");
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL

**Step 3: Implement `AnalysisEngine`**

```csharp
// src/MemoryLens.Mcp/Analysis/AnalysisEngine.cs
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;

namespace MemoryLens.Mcp.Analysis;

public class AnalysisEngine(MemoryLensConfig config)
{
    private static readonly IRule[] BuiltInRules =
    [
        new ML001_EventHandlerLeak(),
        new ML002_StaticCollectionGrowth(),
        new ML003_DisposableLeak(),
        new ML004_LohFragmentation(),
        new ML005_Gen2Retention(),
        new ML006_ExcessiveAllocations(),
        new ML007_ClosureRetention(),
        new ML008_ArrayResizing(),
        new ML009_FinalizerWithoutDispose(),
        new ML010_StringInterning()
    ];

    public IReadOnlyList<IRule> GetActiveRules()
    {
        return BuiltInRules
            .Where(r =>
            {
                if (config.Rules.TryGetValue(r.Id, out var ruleOverride))
                    return ruleOverride.Enabled;
                return true;
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RuleFinding>> AnalyzeAsync(
        SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        foreach (var rule in GetActiveRules())
        {
            var ruleFindings = await rule.EvaluateAsync(context, ct);

            // Apply severity overrides
            if (config.Rules.TryGetValue(rule.Id, out var ruleOverride) &&
                ruleOverride.Severity != null)
            {
                ruleFindings = ruleFindings
                    .Select(f => f with { Severity = ruleOverride.Severity })
                    .ToList();
            }

            // Apply ignore patterns
            ruleFindings = ruleFindings
                .Where(f => !config.Ignore.Any(pattern =>
                    MatchesIgnorePattern(f.Evidence.Type, pattern)))
                .ToList();

            findings.AddRange(ruleFindings);
        }

        return findings.OrderByDescending(f => SeverityOrder(f.Severity)).ToList();
    }

    private static bool MatchesIgnorePattern(string typeName, string pattern)
    {
        var regex = "^" + pattern.Replace(".", "\\.").Replace("*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(typeName, regex);
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };
}
```

**Step 4: Implement MCP tools**

```csharp
// src/MemoryLens.Mcp/Tools/AnalyzeTool.cs
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class AnalyzeTool(AnalysisEngine analysisEngine)
{
    [McpServerTool, Description(
        "Runs memory analysis rules against a snapshot. " +
        "Returns findings with severity, evidence, and concrete code fix suggestions.")]
    public async Task<string> analyze(
        [Description("Snapshot ID from snapshot or compare_snapshots")] string snapshotId,
        [Description("Path to .memorylens.json config file")] string? rulesPath = null,
        CancellationToken ct = default)
    {
        var context = new SnapshotAnalysisContext(
            snapshotId, null, null, null, false,
            Directory.GetCurrentDirectory());

        var findings = await analysisEngine.AnalyzeAsync(context, ct);

        return JsonSerializer.Serialize(findings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
```

```csharp
// src/MemoryLens.Mcp/Tools/GetRulesTool.cs
using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class GetRulesTool(AnalysisEngine analysisEngine)
{
    [McpServerTool, Description(
        "Lists all active memory analysis rules (built-in + user overrides).")]
    public string get_rules(
        [Description("Path to .memorylens.json config file")] string? rulesPath = null)
    {
        var rules = analysisEngine.GetActiveRules()
            .Select(r => new { r.Id, r.Title, r.Severity, r.Category });

        return JsonSerializer.Serialize(rules, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
```

**Step 5: Register in DI**

Add to `Program.cs`:

```csharp
builder.Services.AddSingleton<MemoryLensConfig>(sp =>
    ConfigLoader.LoadFromPath(
        Path.Combine(Directory.GetCurrentDirectory(), ".memorylens.json")));
builder.Services.AddSingleton<AnalysisEngine>();
```

**Step 6: Run tests**

```bash
dotnet test
```

Expected: PASS

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add analyze and get_rules tools with analysis engine"
```

---

### Task 10: Claude Skill — `SKILL.md`

**Files:**
- Create: `skills/memorylens/SKILL.md`

**Step 1: Write the skill file**

```markdown
---
name: memorylens
description: Use when performing memory analysis on .NET applications, investigating memory leaks, or when systematic-debugging identifies a memory-related issue
---

# MemoryLens — Memory Profiling Skill

## Prerequisites

This skill requires the memorylens MCP server tools (`ensure_dotmemory`, `list_processes`, `snapshot`, `compare_snapshots`, `analyze`, `get_rules`).

Check if `ensure_dotmemory` is available as an MCP tool. If not, this skill is inert — inform the user to install the memorylens plugin.

## When to Use

- **On demand**: User requests memory analysis (`/memorylens`)
- **During debugging**: When `systematic-debugging` identifies symptoms like high memory usage, OutOfMemoryException, slow GC, or growing memory over time
- **During brainstorming**: When designing performance-sensitive features that involve caching, event systems, or long-lived objects

## Announce Line

> "MemoryLens activated. I'll profile your application's memory and suggest concrete fixes."

## Workflow

### Step 1: Ensure tooling

Call `ensure_dotmemory`. If it fails, stop and report the error.

### Step 2: Identify target

Ask the user what to profile:
- **Running process**: Call `list_processes` and let user pick
- **Launch command**: User provides a `dotnet run` command or similar
- **Both**: Attach to running OR launch — user's choice

### Step 3: Choose profiling mode

Based on the user's ask:
- **"How does my memory look?"** → Single `snapshot`
- **"Is there a leak?"** / **"What changed?"** → `compare_snapshots`
- **Unclear** → Ask: "Do you want a single snapshot of current state, or a before/after comparison to detect growth?"

### Step 4: Capture

Execute `snapshot` or `compare_snapshots` with the target parameters.

### Step 5: Analyze

Call `analyze` with the returned `snapshotId`. If user has a `.memorylens.json`, pass the `rulesPath`.

### Step 6: Apply fixes

For each finding with a `suggestion`:
1. Present the finding (rule, severity, description, evidence)
2. Show the suggested code change
3. Ask user for approval
4. Apply via Edit tool

Order: critical findings first, then high, medium, low.

### Step 7: Summary

After all findings are addressed, present a summary:
- Total findings by severity
- Fixes applied
- Remaining items (if user skipped any)

## Integration with systematic-debugging

When invoked from systematic-debugging, skip step 2 questioning — the debugger already knows the target process. Use the process context from the debugging session.

**Memory smell indicators** (trigger MemoryLens from debugging):
- `OutOfMemoryException`
- "high memory" or "memory leak" in user description
- GC pressure symptoms (frequent gen2 collections)
- Process memory growing over time

## Integration with brainstorming

When brainstorming features involving:
- Event systems → Reference ML001 (event handler leaks)
- Caching → Reference ML002 (static collection growth), ML005 (gen2 retention)
- IDisposable → Reference ML003, ML009
- Large data → Reference ML004 (LOH fragmentation), ML008 (array resizing)

Use rule knowledge to inform design questions and approach proposals.

## Red Flags

1. **Profiling IDE or tooling processes** — Never. `list_processes` excludes them, but if a user asks to attach to devenv/rider/Code, refuse and explain why.
2. **Running compare_snapshots with very long waits** — Warn if `waitAfter` > 60000ms. Long profiling sessions have overhead.
3. **Applying suggestions without user approval** — Always present and ask. Memory fixes can change behavior.
```

**Step 2: Commit**

```bash
git add skills/
git commit -m "feat: add memorylens Claude skill"
```

---

### Task 11: Marketplace Plugin Packaging

**Files:**
- Create: `.claude-plugin/marketplace.json`
- Create: `.claude-plugin/plugin.json`
- Create: `.mcp.json`

**Step 1: Create `.claude-plugin/marketplace.json`**

```json
{
  "$schema": "https://anthropic.com/claude-code/marketplace.schema.json",
  "name": "memorylens-mcp",
  "description": "On-demand .NET memory profiling with concrete code fix suggestions — powered by JetBrains dotMemory",
  "version": "0.1.0",
  "owner": {
    "name": "Marcel Roozekrans"
  },
  "plugins": [
    {
      "name": "memorylens",
      "source": "./",
      "description": "Memory profiling MCP server with built-in rules and AI-actionable code suggestions for .NET applications",
      "version": "0.1.0",
      "author": {
        "name": "Marcel Roozekrans"
      },
      "repository": "https://github.com/MarcelRoozekrans/memorylens-mcp",
      "license": "MIT",
      "keywords": ["memory", "profiling", "dotmemory", "mcp", "dotnet", "leak-detection"],
      "category": "code-quality",
      "tags": ["memory", "profiling", "dotnet", "mcp"]
    }
  ]
}
```

**Step 2: Create `.claude-plugin/plugin.json`**

```json
{
  "name": "memorylens",
  "version": "0.1.0",
  "description": "On-demand .NET memory profiling with concrete code fix suggestions — powered by JetBrains dotMemory",
  "author": {
    "name": "Marcel Roozekrans",
    "url": "https://github.com/MarcelRoozekrans"
  },
  "homepage": "https://github.com/MarcelRoozekrans/memorylens-mcp",
  "repository": "https://github.com/MarcelRoozekrans/memorylens-mcp",
  "license": "MIT",
  "keywords": ["memory", "profiling", "dotmemory", "mcp", "dotnet", "leak-detection"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json"
}
```

**Step 3: Create `.mcp.json`**

```json
{
  "memorylens": {
    "command": "dotnet",
    "args": ["run", "--project", "src/MemoryLens.Mcp"]
  }
}
```

**Step 4: Commit**

```bash
git add .claude-plugin/ .mcp.json
git commit -m "feat: add marketplace plugin packaging"
```

---

### Task 12: README & LICENSE

**Files:**
- Create: `README.md`
- Create: `LICENSE`

**Step 1: Create `LICENSE`**

MIT license with `Marcel Roozekrans` and year `2026`.

**Step 2: Create `README.md`**

Include:
- What MemoryLens does (one paragraph)
- Installation (`claude plugin add memorylens from gh:MarcelRoozekrans/memorylens-mcp`)
- Prerequisites (.NET 9 SDK)
- Available MCP tools (table from design doc)
- Built-in rules (table from design doc)
- Configuration (`.memorylens.json` example)
- Usage examples (single snapshot, comparison)

**Step 3: Commit**

```bash
git add README.md LICENSE
git commit -m "docs: add README and MIT license"
```

---

### Task 13: Final Integration Verification

**Step 1: Full build**

```bash
dotnet build
```

Expected: Build succeeded.

**Step 2: Run all tests**

```bash
dotnet test
```

Expected: All tests pass.

**Step 3: Verify GitVersion**

```bash
dotnet gitversion
```

Expected: Valid SemVer output.

**Step 4: Verify MCP server starts**

```bash
dotnet run --project src/MemoryLens.Mcp
```

Expected: Server starts and waits for stdio input (MCP protocol).

**Step 5: Verify marketplace structure**

Check that `.claude-plugin/marketplace.json`, `.claude-plugin/plugin.json`, `.mcp.json`, and `skills/memorylens/SKILL.md` all exist.

**Step 6: Commit any fixes**

```bash
git add -A
git commit -m "chore: final integration verification"
```
