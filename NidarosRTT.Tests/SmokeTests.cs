using FluentAssertions;
using Xunit;

namespace NidarosRTT.Tests;

public class SmokeTests
{
    [Fact]
    public void True_is_true()
    {
        true.Should().BeTrue();
    }
}
