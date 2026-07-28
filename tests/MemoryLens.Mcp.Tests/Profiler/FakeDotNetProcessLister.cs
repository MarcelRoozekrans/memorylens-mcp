using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeDotNetProcessLister(params DotNetProcess[] processes) : IDotNetProcessLister
{
    public IReadOnlyList<DotNetProcess> ListProcesses() => processes;
}
