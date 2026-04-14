using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeDotMemoryAutoInstaller(
    string? cachedPath = null,
    string? installPath = null,
    string? unsupportedMessage = null) : IDotMemoryAutoInstaller
{
    private bool _installed;

    public int InstallLatestAsyncCallCount { get; private set; }

    public Task<string?> GetCachedPathAsync(CancellationToken ct)
    {
        // After InstallLatestAsync has been called successfully, return the installPath as cached.
        var result = _installed ? installPath : cachedPath;
        return Task.FromResult(result);
    }

    public Task<string?> InstallLatestAsync(CancellationToken ct)
    {
        InstallLatestAsyncCallCount++;
        if (installPath is not null)
            _installed = true;
        return Task.FromResult(installPath);
    }

    public string? GetUnsupportedPlatformMessage() => unsupportedMessage;
}
