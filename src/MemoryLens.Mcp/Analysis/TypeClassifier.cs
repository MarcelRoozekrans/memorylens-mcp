namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Name-based classification of heap types. Independent of how the heap was
/// collected, so it outlives the report parser it was lifted from.
/// </summary>
public static class TypeClassifier
{
    private static readonly HashSet<string> KnownDisposableTypes =
    [
        "System.IO.FileStream",
        "System.IO.StreamReader",
        "System.IO.StreamWriter",
        "System.IO.MemoryStream",
        "System.IO.BinaryReader",
        "System.IO.BinaryWriter",
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpResponseMessage",
        "System.Net.Http.HttpRequestMessage",
        "System.Net.Sockets.Socket",
        "System.Net.Sockets.TcpClient",
        "System.Net.Sockets.TcpListener",
        "System.Data.SqlClient.SqlConnection",
        "System.Data.SqlClient.SqlCommand",
        "System.Data.SqlClient.SqlDataReader",
        "Microsoft.Data.SqlClient.SqlConnection",
        "Microsoft.Data.SqlClient.SqlCommand",
        "Microsoft.Data.SqlClient.SqlDataReader",
        "System.Threading.CancellationTokenSource",
        "System.Threading.Timer",
        "System.Threading.SemaphoreSlim",
        "System.Threading.ManualResetEventSlim",
        "System.Security.Cryptography.RSA",
        "System.Security.Cryptography.Aes",
    ];

    public static bool IsLikelyDisposable(string typeName)
    {
        if (KnownDisposableTypes.Contains(typeName))
            return true;

        return typeName.Contains("Stream", StringComparison.Ordinal)
            || typeName.Contains("Connection", StringComparison.Ordinal)
            || typeName.Contains("Reader", StringComparison.Ordinal)
            || typeName.Contains("Writer", StringComparison.Ordinal)
            || typeName.Contains("Client", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Handle", StringComparison.Ordinal);
    }

    public static bool IsLikelyFinalizable(string typeName)
    {
        return typeName.Contains("SafeHandle", StringComparison.Ordinal)
            || typeName.Contains("FileStream", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Timer", StringComparison.Ordinal);
    }
}
