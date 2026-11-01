using FluentAssertions;
using GeoAssets.Workflow.Infrastructure;
using Xunit;

namespace GeoAssets.Workflow.Tests.Infrastructure;

public class ManholeAuditTests
{
    [Fact]
    public void SubDuctOccupancyPercentage_NoSubDucts_IsZero()
    {
        var audit = new ManholeAudit { SubDucts = [] };

        audit.SubDuctOccupancyPercentage.Should().Be(0);
    }

    [Fact]
    public void SubDuctOccupancyPercentage_SomeOccupied_ComputesPercentageOfOccupied()
    {
        var audit = new ManholeAudit
        {
            SubDucts =
            [
                new SubDuct(32, Occupied: true),
                new SubDuct(32, Occupied: true),
                new SubDuct(40, Occupied: false),
                new SubDuct(40, Occupied: false),
            ],
        };

        audit.SubDuctOccupancyPercentage.Should().Be(50.0);
    }

    [Fact]
    public void SubDuctOccupancyPercentage_AllOccupied_IsOneHundred()
    {
        var audit = new ManholeAudit { SubDucts = [new SubDuct(32, Occupied: true)] };

        audit.SubDuctOccupancyPercentage.Should().Be(100.0);
    }

    [Fact]
    public void SubDuctOccupancyPercentage_IsIndependentFromBoreOccupancyPercentage()
    {
        // The two are deliberately separate facts — see ManholeAudit's own doc comment.
        var audit = new ManholeAudit
        {
            BoreOccupancyPercentage = 90.0,
            SubDucts = [new SubDuct(32, Occupied: false)],
        };

        audit.SubDuctOccupancyPercentage.Should().Be(0.0);
        audit.BoreOccupancyPercentage.Should().Be(90.0);
    }
}
