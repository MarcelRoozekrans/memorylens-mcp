namespace MemoryLens.Mcp.TestSupport;

/// <summary>
/// A unique temp directory that deletes itself. Every test gets its own, so
/// nothing shares state through the filesystem.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "memorylens-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp dir must never fail a test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
