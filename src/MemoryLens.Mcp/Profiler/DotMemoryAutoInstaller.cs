using System.Runtime.InteropServices;

namespace MemoryLens.Mcp.Profiler;

public class DotMemoryAutoInstaller(HttpClient httpClient, string? cacheRoot = null) : IDotMemoryAutoInstaller
{
    private readonly string _cacheRoot = cacheRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memorylens", "tools", "dotmemory");

    // --- Platform detection ---

    public static string? GetRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64   => "windows-x64",
                Architecture.X86   => "windows-x86",
                Architecture.Arm64 => "windows-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsLinux())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm)
                return "linux-arm";

            bool isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            return BuildLinuxRid(isArm64, IsMusl());
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64   => "macos-x64",
                Architecture.Arm64 => "macos-arm64",
                _ => null
            };
        }

        return null;
    }

    public static string BuildLinuxRid(bool isArm64, bool isMusl)
    {
        var arch = isArm64 ? "arm64" : "x64";
        return isMusl ? $"linux-musl-{arch}" : $"linux-{arch}";
    }

    internal static bool IsMusl() =>
        File.Exists("/lib/ld-musl-x86_64.so.1") ||
        File.Exists("/lib/ld-musl-aarch64.so.1");

    public string? GetUnsupportedPlatformMessage()
    {
        if (GetRid() is not null)
            return null;

        var current = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        return $"Platform '{current}' is not supported by JetBrains dotMemory Console. " +
               "Set DOTMEMORY_PATH to point to your dotMemory installation. " +
               "See README for details.";
    }

    // --- Cache reading ---

    public async Task<string?> GetCachedPathAsync(CancellationToken ct)
    {
        var currentFile = Path.Combine(_cacheRoot, "current.txt");
        if (!File.Exists(currentFile))
            return null;

        var version = (await File.ReadAllTextAsync(currentFile, ct).ConfigureAwait(false)).Trim();
        var versionDir = Path.Combine(_cacheRoot, version);
        var exePath = FindExecutable(versionDir);
        if (exePath is null || !File.Exists(exePath))
            return null;

        // Re-apply the execute bits on a cache hit as well. A cache extracted before
        // MakeToolsExecutable walked the whole tree has an executable entry point but
        // a non-executable runtime-dotnet.sh, and resolution would otherwise hand back
        // a path that fails with "Permission denied" forever.
        try { MakeToolsExecutable(versionDir); }
        catch (IOException) { /* best effort — a read-only cache still resolves */ }
        catch (UnauthorizedAccessException) { /* ditto */ }

        return exePath;
    }

    private static string? FindExecutable(string versionDir)
    {
        var toolsDir = Path.Combine(versionDir, "tools");
        if (!Directory.Exists(toolsDir))
            return null;

        var candidates = OperatingSystem.IsWindows()
            ? new[] { "dotMemory.exe" }
            : new[] { "dotMemory.sh", "dotMemory" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(toolsDir, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    // --- Download & install ---

    // Not thread-safe: concurrent calls to the same cache root may corrupt extraction.
    public async Task<string?> InstallLatestAsync(CancellationToken ct)
    {
        var rid = GetRid();
        if (rid is null)
            return null;

        var packageId = $"jetbrains.dotmemory.console.{rid}";

        string? version;
        try
        {
            version = await FetchLatestVersionAsync(packageId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }

        if (version is null)
            return null;

        var versionDir = Path.Combine(_cacheRoot, version);

        string? exePath = null;
        for (var attempt = 0; attempt < 2 && exePath is null; attempt++)
        {
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);

            try
            {
                await DownloadAndExtractAsync(packageId, version, versionDir, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { continue; }

            exePath = FindExecutable(versionDir);
        }

        if (exePath is null || !File.Exists(exePath))
        {
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);
            return null;
        }

        try { MakeToolsExecutable(versionDir); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);
            return null;
        }

        Directory.CreateDirectory(_cacheRoot);
        await File.WriteAllTextAsync(Path.Combine(_cacheRoot, "current.txt"), version, ct).ConfigureAwait(false);

        return exePath;
    }

    internal async Task<string?> FetchLatestVersionAsync(string packageId, CancellationToken ct)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/index.json";
        var json = await httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions");
        var length = versions.GetArrayLength();
        if (length == 0)
            return null;
        return versions[length - 1].GetString();
    }

    private async Task DownloadAndExtractAsync(string packageId, string version, string versionDir, CancellationToken ct)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg";
        var tempFile = Path.GetTempFileName();
        try
        {
            using var response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var fs = File.Create(tempFile);
                await using (fs.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
                    await fs.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            Directory.CreateDirectory(versionDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, versionDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Restores the execute bit across the extracted package.
    /// <para>
    /// ZipFile.ExtractToDirectory discards Unix permission bits, so everything in a
    /// .nupkg arrives as 0644. Chmodding only the entry point is not enough: the
    /// dotMemory launcher execs runtime-dotnet.sh, which in turn execs a native host,
    /// and the failure surfaces as "Permission denied" on that second hop.
    /// </para>
    /// </summary>
    internal static void MakeToolsExecutable(string versionDir)
    {
        if (OperatingSystem.IsWindows())
            return;

        foreach (var path in Directory.EnumerateFiles(versionDir, "*", SearchOption.AllDirectories))
        {
            // Cheap first: skip anything already executable so the cache-hit path does
            // not re-stat and re-chmod the whole package on every resolve.
            if (File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute))
                continue;

            if (!NeedsExecuteBit(path))
                continue;

            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// Whether a file is something the OS must be able to exec, decided by its leading
    /// bytes rather than its name.
    /// <para>
    /// Naming is not a usable signal here: JetBrains ships native helpers with
    /// dotted names (JetBrains.Profiler.PdbServer), so Path.GetExtension reports
    /// ".PdbServer" and an "extensionless means native" rule silently misses them.
    /// Managed assemblies start with "MZ" and are launched through the host, so they
    /// are excluded for free.
    /// </para>
    /// </summary>
    internal static bool NeedsExecuteBit(string path)
    {
        Span<byte> header = stackalloc byte[4];

        int read;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        // Shebang: "#!"
        if (read >= 2 && header[0] == 0x23 && header[1] == 0x21)
            return true;

        if (read < 4)
            return false;

        // ELF: 0x7F 'E' 'L' 'F'
        if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
            return true;

        // Mach-O, 32/64-bit either endianness, plus universal binaries.
        var magic = (uint)(header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3]);
        return magic is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE or 0xCAFEBABE;
    }
}
