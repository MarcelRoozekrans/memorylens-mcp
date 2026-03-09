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

    private string? FindSnapshotFile(string snapshotName)
    {
        if (!Directory.Exists(_snapshotDir))
            return null;

        return Directory.GetFiles(_snapshotDir, $"{snapshotName}*")
            .FirstOrDefault();
    }
}
