using FluentAssertions;
using GeoAssets.Workflow.Erp.Totvs;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Totvs;

public class TotvsPayrollExportMapperTests
{
    private static readonly DateTime PostedAt = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Map_ResolvedTechnician_ProducesLineWithCorrectTotal()
    {
        var posting = new ApprovedUsPosting("order-1", "tech-1", "us-100", 2m, 50m, PostedAt);

        var (lines, unresolved) = TotvsPayrollExportMapper.Map([posting]);

        lines.Should().ContainSingle();
        unresolved.Should().BeEmpty();
        var line = lines[0];
        line.TechnicianId.Should().Be("tech-1");
        line.ServiceOrderId.Should().Be("order-1");
        line.UsCode.Should().Be("us-100");
        line.Quantity.Should().Be(2m);
        line.UnitValue.Should().Be(50m);
        line.TotalValue.Should().Be(100m);
        line.PostedAtUtc.Should().Be(PostedAt);
    }

    [Fact]
    public void Map_NullTechnicianId_IsUnresolvedNotExported()
    {
        var posting = new ApprovedUsPosting("order-1", null, "us-100", 2m, 50m, PostedAt);

        var (lines, unresolved) = TotvsPayrollExportMapper.Map([posting]);

        lines.Should().BeEmpty();
        unresolved.Should().ContainSingle().Which.Should().Be(posting);
    }

    [Fact]
    public void Map_WhitespaceTechnicianId_IsUnresolvedNotExported()
    {
        var posting = new ApprovedUsPosting("order-1", "   ", "us-100", 2m, 50m, PostedAt);

        var (lines, unresolved) = TotvsPayrollExportMapper.Map([posting]);

        lines.Should().BeEmpty();
        unresolved.Should().ContainSingle();
    }

    [Fact]
    public void Map_MixedResolvedAndUnresolved_SplitsCorrectly()
    {
        var resolved = new ApprovedUsPosting("order-1", "tech-1", "us-100", 1m, 10m, PostedAt);
        var unresolvedPosting = new ApprovedUsPosting("order-2", null, "us-200", 1m, 10m, PostedAt);

        var (lines, unresolved) = TotvsPayrollExportMapper.Map([resolved, unresolvedPosting]);

        lines.Should().ContainSingle(l => l.ServiceOrderId == "order-1");
        unresolved.Should().ContainSingle(p => p.ServiceOrderId == "order-2");
    }
}
