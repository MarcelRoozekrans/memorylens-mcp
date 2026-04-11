using System;
using System.Threading;
using MemoryLens.Mcp.Profiler;

// Set DOTMEMORY_PATH
Environment.SetEnvironmentVariable("DOTMEMORY_PATH", "/home/gospodin/.local/share/JetBrains/Toolbox/apps/rider/tools/profiler/dotMemory.sh");

// Create process runner
var processRunner = new ProcessRunner();
var toolManager = new DotMemoryToolManager(processRunner);

Console.WriteLine("Testing EnsureInstalledAsync with DOTMEMORY_PATH...");
var status = await toolManager.EnsureInstalledAsync(CancellationToken.None);

Console.WriteLine($"IsInstalled: {status.IsInstalled}");
Console.WriteLine($"Version: {status.Version}");
Console.WriteLine($"Message: {status.Message}");

if (status.IsInstalled)
{
    Console.WriteLine("\n✓ SUCCESS: dotMemory CLI is available via DOTMEMORY_PATH");
    Environment.Exit(0);
}
else
{
    Console.WriteLine("\n✗ FAILED: dotMemory CLI is not available");
    Environment.Exit(1);
}
