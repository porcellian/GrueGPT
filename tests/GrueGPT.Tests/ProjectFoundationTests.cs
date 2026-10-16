using GrueGPT.Core;
using Xunit;

namespace GrueGPT.Tests;

public sealed class ProjectFoundationTests
{
    [Fact]
    public void CoreAssemblyIsLoadable()
    {
        Assert.Equal("GrueGPT.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
