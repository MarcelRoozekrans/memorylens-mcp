using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MemoryLens.Mcp.TestSupport;

/// <summary>
/// Drives an MCP server over newline-delimited JSON-RPC 2.0 on stdio.
/// The transport is parameterised so the same client can target the built DLL,
/// a Docker image, or the npm shim.
/// </summary>
public sealed class McpStdioClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly TimeSpan _timeout;
    private readonly StringBuilder _stderr = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextId;

    private McpStdioClient(Process process, TimeSpan timeout)
    {
        _process = process;
        _timeout = timeout;
    }

    /// <summary>Launches the server DLL copied next to the test assembly.</summary>
    public static McpStdioClient StartServer(TimeSpan? timeout = null)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "MemoryLens.Mcp.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException(
                $"Server not found at {dll}. The test project needs a ProjectReference to MemoryLens.Mcp.", dll);

        return Start("dotnet", $"\"{dll}\"", timeout);
    }

    public static McpStdioClient Start(string fileName, string arguments, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var client = new McpStdioClient(process, timeout ?? TimeSpan.FromSeconds(60));

        // The server logs to stderr; drain it so a full pipe can never deadlock the child.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (client._stderr) client._stderr.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        return client;
    }

    /// <summary>Everything the server wrote to stderr, for failure messages.</summary>
    public string StandardError
    {
        get { lock (_stderr) return _stderr.ToString(); }
    }

    public async Task<JsonElement> InitializeAsync(CancellationToken ct)
    {
        var response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "memorylens-integration-tests", version = "1.0" },
        }, ct).ConfigureAwait(false);

        await SendNotificationAsync("notifications/initialized", ct).ConfigureAwait(false);
        return response;
    }

    public async Task<IReadOnlyList<string>> ListToolNamesAsync(CancellationToken ct)
    {
        var result = await SendRequestAsync("tools/list", new { }, ct).ConfigureAwait(false);
        return result.GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public Task<JsonElement> CallToolAsync(string name, object arguments, CancellationToken ct) =>
        SendRequestAsync("tools/call", new { name, arguments }, ct);

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        await WriteLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        }), ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        // Skip notifications and any response whose id is not ours.
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Server closed stdout while awaiting '{method}'. stderr:\n{StandardError}");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idElement) || idElement.GetInt32() != id)
                continue;

            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException(
                    $"'{method}' returned a JSON-RPC error: {error.GetRawText()}");

            return root.GetProperty("result").Clone();
        }
    }

    private Task SendNotificationAsync(string method, CancellationToken ct) =>
        WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method }), ct);

    private async Task WriteLineAsync(string json, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.WriteAsync("\n".AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            _writeLock.Dispose();
            _process.Dispose();
        }
    }
}
