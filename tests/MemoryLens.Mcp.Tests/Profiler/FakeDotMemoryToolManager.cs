using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeDotMemoryToolManager : DotMemoryToolManager
{
    private readonly DotMemoryCommand? _command;

    public FakeDotMemoryToolManager(IProcessRunner processRunner, DotMemoryCommand? command = null)
        : base(processRunner)
    {
        _command = command ?? new DotMemoryCommand("dotnet-dotmemory", "", "fake command", "1.0.0");
    }

    public new Task<DotMemoryCommand?> ResolveCommandAsync(CancellationToken ct = default)
    {
        return Task.FromResult<DotMemoryCommand?>(_command);
    }
}
