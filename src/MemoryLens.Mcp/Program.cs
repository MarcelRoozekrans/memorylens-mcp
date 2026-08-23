using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<ProcessFilter>();
builder.Services.AddSingleton<IDotNetProcessLister>(_ => new DiagnosticPortProcessLister());
builder.Services.AddSingleton<MemoryLensConfig>(sp =>
    ConfigLoader.LoadFromPath(Path.Combine(Directory.GetCurrentDirectory(), ".memorylens.json")));
builder.Services.AddSingleton<IHeapCollector>(_ => new HeapCollector());
builder.Services.AddSingleton<ISnapshotStore>(_ => new SnapshotStore());
builder.Services.AddSingleton<ISnapshotReader, SnapshotReader>();
builder.Services.AddSingleton<AnalysisEngine>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
