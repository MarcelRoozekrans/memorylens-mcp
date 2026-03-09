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
