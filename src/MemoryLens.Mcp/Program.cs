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

builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddHttpClient<DotMemoryAutoInstaller>();
builder.Services.AddSingleton<IDotMemoryAutoInstaller>(sp =>
    new DotMemoryAutoInstaller(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DotMemoryAutoInstaller))));
builder.Services.AddSingleton<DotMemoryToolManager>();
builder.Services.AddSingleton<ProcessFilter>();
builder.Services.AddSingleton<SnapshotManager>();
builder.Services.AddSingleton<MemoryLensConfig>(sp =>
    ConfigLoader.LoadFromPath(Path.Combine(Directory.GetCurrentDirectory(), ".memorylens.json")));
builder.Services.AddSingleton<IDotMemoryAnalyzer, DotMemoryAnalyzer>();
builder.Services.AddSingleton<AnalysisEngine>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
