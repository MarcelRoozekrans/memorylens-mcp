using System.Globalization;

namespace MemoryLens.Mcp.Profiler;

public class SnapshotManager(
    IProcessRunner processRunner,
    ProcessFilter processFilter,
    DotMemoryToolManager dotMemoryToolManager)
{
    private readonly string _snapshotDir = Path.Combine(Path.GetTempPath(), "memorylens-snapshots");

    public async Task<SnapshotResult> TakeSnapshotAsync(
        int? pid = null,
        string? processName = null,
        string? command = null,
        int? durationSeconds = null,
        CancellationToken ct = default)
    {
        if (processName is not null && processFilter.IsExcluded(processName, ""))
            return new SnapshotResult(false, null, null, $"Process '{processName}' is excluded from profiling.");

        Directory.CreateDirectory(_snapshotDir);

        var snapshotId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var snapshotName = $"snapshot-{snapshotId}";

        string arguments;

        if (command is not null)
        {
            arguments = $"start --save-to-dir={_snapshotDir} --snapshot-name={snapshotName} -- {command}";
        }
        else
        {
            var target = pid.HasValue ? pid.Value.ToString(CultureInfo.InvariantCulture) : processName!;
            arguments = $"get-snapshot {target} --save-to-dir={_snapshotDir} --snapshot-name={snapshotName}";
        }

        if (durationSeconds.HasValue && durationSeconds.Value > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds.Value), ct).ConfigureAwait(false);
        }

        var commandInfo = await dotMemoryToolManager.ResolveCommandAsync(ct).ConfigureAwait(false);
        if (commandInfo is null)
        {
            return new SnapshotResult(
                false,
                null,
                null,
                "dotMemory CLI not found. Run ensure_dotmemory, set DOTMEMORY_PATH or MEMORYLENS_DOTMEMORY_PATH, or ensure dotMemory is available on PATH.");
        }

        var result = await processRunner
            .RunAsync(commandInfo.FileName, commandInfo.BuildArguments(arguments), ct)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
            return new SnapshotResult(false, null, null, $"dotMemory CLI failed: {result.Error}");

        var snapshotPath = FindSnapshotFile(snapshotName);

        return new SnapshotResult(true, snapshotId, snapshotPath, null);
    }

    public async Task<ComparisonResult> CompareSnapshotsAsync(
        int? pid = null,
        string? processName = null,
        string? command = null,
        int? delaySeconds = null,
        CancellationToken ct = default)
    {
        if (processName is not null && processFilter.IsExcluded(processName, ""))
            return new ComparisonResult(false, null, null, null, 0, $"Process '{processName}' is excluded from profiling.");

        Directory.CreateDirectory(_snapshotDir);

        var comparisonId = Guid.NewGuid().ToString("N")[..8];
        var beforeName = $"before-{comparisonId}";
        var afterName = $"after-{comparisonId}";

        var commandInfo = await dotMemoryToolManager.ResolveCommandAsync(ct).ConfigureAwait(false);
        if (commandInfo is null)
        {
            return new ComparisonResult(
                false,
                null,
                null,
                null,
                0,
                "dotMemory CLI not found. Run ensure_dotmemory, set DOTMEMORY_PATH or MEMORYLENS_DOTMEMORY_PATH, or make dotMemory available on PATH.");
        }

        // Take before snapshot
        var beforeArgs = BuildSnapshotArguments(pid, processName, command, beforeName);
        var beforeResult = await processRunner
            .RunAsync(commandInfo.FileName, commandInfo.BuildArguments(beforeArgs), ct)
            .ConfigureAwait(false);

        if (beforeResult.ExitCode != 0)
            return new ComparisonResult(false, null, null, null, 0, $"Before snapshot failed: {beforeResult.Error}");

        var beforePath = FindSnapshotFile(beforeName);

        // Wait between snapshots
        var delay = delaySeconds ?? 10;
        if (delay > 0)
            await Task.Delay(TimeSpan.FromSeconds(delay), ct).ConfigureAwait(false);

        // Take after snapshot
        var afterArgs = BuildSnapshotArguments(pid, processName, command, afterName);
        var afterResult = await processRunner
            .RunAsync(commandInfo.FileName, commandInfo.BuildArguments(afterArgs), ct)
            .ConfigureAwait(false);

        if (afterResult.ExitCode != 0)
            return new ComparisonResult(false, comparisonId, beforePath, null, 1, $"After snapshot failed: {afterResult.Error}");

        var afterPath = FindSnapshotFile(afterName);

        return new ComparisonResult(true, comparisonId, beforePath, afterPath, 2, null);
    }

    private string BuildSnapshotArguments(int? pid, string? processName, string? command, string snapshotName)
    {
        if (command is not null)
            return $"start --save-to-dir={_snapshotDir} --snapshot-name={snapshotName} -- {command}";

        var target = pid.HasValue ? pid.Value.ToString(CultureInfo.InvariantCulture) : processName!;
        return $"get-snapshot {target} --save-to-dir={_snapshotDir} --snapshot-name={snapshotName}";
    }

    private string? FindSnapshotFile(string snapshotName)
    {
        if (!Directory.Exists(_snapshotDir))
            return null;

        return Directory.GetFiles(_snapshotDir, $"{snapshotName}*")
            .FirstOrDefault();
    }
}
