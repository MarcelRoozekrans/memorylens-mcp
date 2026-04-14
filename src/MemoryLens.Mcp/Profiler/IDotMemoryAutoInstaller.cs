namespace MemoryLens.Mcp.Profiler;

public interface IDotMemoryAutoInstaller
{
    /// <summary>Returns the path to the cached dotMemory executable, or null if not cached.</summary>
    Task<string?> GetCachedPathAsync(CancellationToken ct);

    /// <summary>Downloads and caches the latest dotMemory for the current platform.
    /// Returns the executable path on success, null on failure (network error, unsupported platform).</summary>
    Task<string?> InstallLatestAsync(CancellationToken ct);

    /// <summary>Returns a human-readable message if the current platform is not supported by
    /// JetBrains dotMemory Console, or null if the platform is supported.</summary>
    string? GetUnsupportedPlatformMessage();
}
