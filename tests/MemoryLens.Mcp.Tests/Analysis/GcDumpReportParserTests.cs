using MemoryLens.Mcp.Analysis;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class GcDumpReportParserTests
{
    private const string SampleReport = """
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4     1234       56789 System.String
        00007ff8a1b2c3e0       50      200000 System.Byte[]
        00007ff8a1b2c3f0       15     1500000 System.Object[]
        Total     1299     1756789
        """;

    [Fact]
    public void Parse_ExtractsTypes()
    {
        var data = GcDumpReportParser.Parse(SampleReport);

        Assert.Equal(3, data.Types.Count);
        Assert.Contains(data.Types, t => t.FullName == "System.String");
        Assert.Contains(data.Types, t => t.FullName == "System.Byte[]");
        Assert.Contains(data.Types, t => t.FullName == "System.Object[]");
    }

    [Fact]
    public void Parse_ExtractsInstanceCountAndSize()
    {
        var data = GcDumpReportParser.Parse(SampleReport);

        var stringType = data.Types.First(t => t.FullName == "System.String");
        Assert.Equal(1234, stringType.InstanceCount);
        Assert.Equal(56789, stringType.TotalBytes);
    }

    [Fact]
    public void Parse_ExtractsTotalHeapBytes()
    {
        var data = GcDumpReportParser.Parse(SampleReport);

        Assert.Equal(1756789, data.Heap.TotalBytes);
    }

    [Fact]
    public void Parse_DetectsLargeObjectHeap()
    {
        // Object[] has 15 instances at 1500000 bytes = 100000 avg > 85000 threshold
        var data = GcDumpReportParser.Parse(SampleReport);

        var objectArray = data.Types.First(t => t.FullName == "System.Object[]");
        Assert.True(objectArray.IsLargeObjectHeap);

        // String has 1234 instances at 56789 bytes = ~46 avg, not LOH
        var stringType = data.Types.First(t => t.FullName == "System.String");
        Assert.False(stringType.IsLargeObjectHeap);
    }

    [Fact]
    public void Parse_DetectsDisposableTypes()
    {
        var report = """
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4       10       50000 System.IO.FileStream
        00007ff8a1b2c3e0        5       10000 MyApp.Services.UserService
        """;

        var data = GcDumpReportParser.Parse(report);

        var fileStream = data.Types.First(t => t.FullName == "System.IO.FileStream");
        Assert.True(fileStream.ImplementsIDisposable);

        var userService = data.Types.First(t => t.FullName == "MyApp.Services.UserService");
        Assert.False(userService.ImplementsIDisposable);
    }

    [Fact]
    public void Parse_CalculatesLohHeapInfo()
    {
        var data = GcDumpReportParser.Parse(SampleReport);

        Assert.True(data.Heap.LargeObjectHeapBytes > 0);
        Assert.True(data.Heap.LargeObjectCount > 0);
    }

    [Fact]
    public void Parse_ReturnsEmptyForEmptyInput()
    {
        var data = GcDumpReportParser.Parse("");

        Assert.Empty(data.Types);
        Assert.Equal(0, data.Heap.TotalBytes);
    }

    [Fact]
    public void Parse_SkipsNonMatchingLines()
    {
        var report = """
        Some header text
        Another line
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4      100        5000 System.String
        Some footer
        """;

        var data = GcDumpReportParser.Parse(report);

        Assert.Single(data.Types);
        Assert.Equal("System.String", data.Types[0].FullName);
    }
}
