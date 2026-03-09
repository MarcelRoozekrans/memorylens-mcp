namespace MemoryLens.Mcp.Profiler;

public class SnapshotManager(IProcessRunner processRunner, ProcessFilter processFilter)
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

        var snapshotId = Guid.NewGuid().ToString("N")[..8];
        var snapshotName = $"snapshot-{snapshotId}";

        string arguments;

        if (command is not null)
        {
            arguments = $"start --save-to-dir={_snapshotDir} --snapshot-name={snapshotName} -- {command}";
        }
        else
        {
            var target = pid.HasValue ? pid.Value.ToString() : processName!;
            arguments = $"get-snapshot {target} --save-to-dir={_snapshotDir} --snapshot-name={snapshotName}";
        }

        if (durationSeconds.HasValue && durationSeconds.Value > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds.Value), ct);
        }

        var result = await processRunner.RunAsync("dotnet-dotmemory", arguments, ct);

        if (result.ExitCode != 0)
            return new SnapshotResult(false, null, null, $"dotnet-dotmemory failed: {result.Error}");

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

        // Take before snapshot
        var beforeArgs = BuildSnapshotArguments(pid, processName, command, beforeName);
        var beforeResult = await processRunner.RunAsync("dotnet-dotmemory", beforeArgs, ct);

        if (beforeResult.ExitCode != 0)
            return new ComparisonResult(false, null, null, null, 0, $"Before snapshot failed: {beforeResult.Error}");

        var beforePath = FindSnapshotFile(beforeName);

        // Wait between snapshots
        var delay = delaySeconds ?? 10;
        if (delay > 0)
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);

        // Take after snapshot
        var afterArgs = BuildSnapshotArguments(pid, processName, command, afterName);
        var afterResult = await processRunner.RunAsync("dotnet-dotmemory", afterArgs, ct);

        if (afterResult.ExitCode != 0)
            return new ComparisonResult(false, comparisonId, beforePath, null, 1, $"After snapshot failed: {afterResult.Error}");

        var afterPath = FindSnapshotFile(afterName);

        return new ComparisonResult(true, comparisonId, beforePath, afterPath, 2, null);
    }

    private string BuildSnapshotArguments(int? pid, string? processName, string? command, string snapshotName)
    {
        if (command is not null)
            return $"start --save-to-dir={_snapshotDir} --snapshot-name={snapshotName} -- {command}";

        var target = pid.HasValue ? pid.Value.ToString() : processName!;
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
