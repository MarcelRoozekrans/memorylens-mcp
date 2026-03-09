using System.Globalization;
using System.Text.RegularExpressions;

namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Parses the text output from 'dotnet-gcdump report' into SnapshotData.
/// Expected format (tab/space-separated columns):
///          MT    Count    TotalSize Class Name
/// 00007ff...    1234        56789 System.String
/// </summary>
public static partial class GcDumpReportParser
{
    // Matches lines like: "00007ff8a1b2c3d4    1234        56789 System.String"
    [GeneratedRegex(@"^\s*[0-9a-fA-F]+\s+(?<count>\d+)\s+(?<size>\d+)\s+(?<name>.+)$", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex TypeLineRegex();

    // Matches the "Total" summary line: "Total    12345   1234567"
    [GeneratedRegex(@"^Total\s+(?<count>\d+)\s+(?<size>\d+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex TotalLineRegex();

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

    public static SnapshotData Parse(string reportOutput)
    {
        var types = new List<TypeInfo>();
        long totalBytes = 0;
        long lohBytes = 0;
        int lohCount = 0;

        foreach (var line in reportOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var totalMatch = TotalLineRegex().Match(trimmed);
            if (totalMatch.Success)
            {
                totalBytes = long.Parse(totalMatch.Groups["size"].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var typeMatch = TypeLineRegex().Match(trimmed);
            if (!typeMatch.Success)
                continue;

            var count = int.Parse(typeMatch.Groups["count"].Value, CultureInfo.InvariantCulture);
            var size = long.Parse(typeMatch.Groups["size"].Value, CultureInfo.InvariantCulture);
            var typeName = typeMatch.Groups["name"].Value.Trim();

            var avgSize = count > 0 ? size / count : 0;
            var isLoh = avgSize >= 85_000;

            if (isLoh)
            {
                lohBytes += size;
                lohCount += count;
            }

            types.Add(new TypeInfo
            {
                FullName = typeName,
                InstanceCount = count,
                TotalBytes = size,
                IsLargeObjectHeap = isLoh,
                ImplementsIDisposable = IsLikelyDisposable(typeName),
                HasFinalizer = IsLikelyFinalizable(typeName),
            });
        }

        return new SnapshotData
        {
            Types = types,
            Heap = new HeapInfo
            {
                TotalBytes = totalBytes > 0 ? totalBytes : types.Sum(t => t.TotalBytes),
                LargeObjectHeapBytes = lohBytes,
                LargeObjectCount = lohCount,
            }
        };
    }

    private static bool IsLikelyDisposable(string typeName)
    {
        if (KnownDisposableTypes.Contains(typeName))
            return true;

        // Heuristic: types with "Stream", "Connection", "Reader", "Writer" in the name
        return typeName.Contains("Stream", StringComparison.Ordinal)
            || typeName.Contains("Connection", StringComparison.Ordinal)
            || typeName.Contains("Reader", StringComparison.Ordinal)
            || typeName.Contains("Writer", StringComparison.Ordinal)
            || typeName.Contains("Client", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Handle", StringComparison.Ordinal);
    }

    private static bool IsLikelyFinalizable(string typeName)
    {
        // Types known to have finalizers
        return typeName.Contains("SafeHandle", StringComparison.Ordinal)
            || typeName.Contains("FileStream", StringComparison.Ordinal)
            || typeName.Contains("Socket", StringComparison.Ordinal)
            || typeName.Contains("Timer", StringComparison.Ordinal);
    }
}
