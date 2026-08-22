using MemoryLens.Mcp.Analysis;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class TypeClassifierTests
{
    [Fact]
    public void IsLikelyDisposable_KnownType_ReturnsTrue()
    {
        Assert.True(TypeClassifier.IsLikelyDisposable("System.IO.FileStream"));
        Assert.True(TypeClassifier.IsLikelyDisposable("System.Threading.Timer"));
    }

    [Fact]
    public void IsLikelyDisposable_HeuristicMatch_ReturnsTrue()
    {
        Assert.True(TypeClassifier.IsLikelyDisposable("MyApp.Data.DbConnection"));
        Assert.True(TypeClassifier.IsLikelyDisposable("MyApp.Io.CustomWriter"));
    }

    [Fact]
    public void IsLikelyDisposable_PlainType_ReturnsFalse()
    {
        Assert.False(TypeClassifier.IsLikelyDisposable("System.String"));
        Assert.False(TypeClassifier.IsLikelyDisposable("MyApp.Models.Customer"));
    }

    [Fact]
    public void IsLikelyFinalizable_MatchesKnownPatterns()
    {
        Assert.True(TypeClassifier.IsLikelyFinalizable("Microsoft.Win32.SafeHandles.SafeFileHandle"));
        Assert.True(TypeClassifier.IsLikelyFinalizable("System.Threading.Timer"));
        Assert.False(TypeClassifier.IsLikelyFinalizable("System.String"));
    }
}
