using System;
using System.Threading;
using MemoryLens.Mcp.Profiler;

// Check if DOTMEMORY_PATH is set
var dotMemoryPath = Environment.GetEnvironmentVariable("DOTMEMORY_PATH");
if (string.IsNullOrWhiteSpace(dotMemoryPath))
{
    Console.WriteLine("Error: DOTMEMORY_PATH environment variable is not set.");
    Console.WriteLine("\nUsage:");
    Console.WriteLine("  Linux/macOS: export DOTMEMORY_PATH=/path/to/dotMemory.sh");
    Console.WriteLine("  Windows:     set DOTMEMORY_PATH=C:\\path\\to\\dotMemory.exe");
    Console.WriteLine("\nThen run this sample again.");
    Environment.Exit(1);
}

// Create process runner
var processRunner = new ProcessRunner();
var toolManager = new DotMemoryToolManager(processRunner);

Console.WriteLine($"Testing EnsureInstalledAsync with DOTMEMORY_PATH={dotMemoryPath}...");
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
