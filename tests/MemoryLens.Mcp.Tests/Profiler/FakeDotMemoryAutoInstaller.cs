using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeDotMemoryAutoInstaller(
    string? cachedPath = null,
    string? installPath = null,
    string? unsupportedMessage = null) : IDotMemoryAutoInstaller
{
    public Task<string?> GetCachedPathAsync(CancellationToken ct) =>
        Task.FromResult(cachedPath);

    public Task<string?> InstallLatestAsync(CancellationToken ct) =>
        Task.FromResult(installPath);

    public string? GetUnsupportedPlatformMessage() => unsupportedMessage;
}
