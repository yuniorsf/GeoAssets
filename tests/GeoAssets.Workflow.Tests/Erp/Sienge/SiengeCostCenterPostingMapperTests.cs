using FluentAssertions;
using GeoAssets.Workflow.Erp.Sienge;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sienge;

public class SiengeCostCenterPostingMapperTests
{
    private static readonly DateTime WorkDate = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime HaulDate = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Map_MachineHoursAndHaulingTickets_ProducesOneLinePerRecord()
    {
        var machineHours = new[] { new MachineHourRecord("cc-1", "us-equip-1", 8m, WorkDate) };
        var haulingTickets = new[] { new HaulingTicketRecord("cc-1", "us-haul-1", 12.5m, HaulDate) };

        var lines = SiengeCostCenterPostingMapper.Map(machineHours, haulingTickets);

        lines.Should().HaveCount(2);
        lines.Should().Contain(l => l.CostCenterCode == "cc-1" && l.UsCode == "us-equip-1" && l.Quantity == 8m && l.OccurredUtc == WorkDate);
        lines.Should().Contain(l => l.CostCenterCode == "cc-1" && l.UsCode == "us-haul-1" && l.Quantity == 12.5m && l.OccurredUtc == HaulDate);
    }

    [Fact]
    public void Map_EmptyInputs_ProducesNoLines()
    {
        var lines = SiengeCostCenterPostingMapper.Map([], []);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void Map_NullMachineHours_Throws()
    {
        var act = () => SiengeCostCenterPostingMapper.Map(null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Map_NullHaulingTickets_Throws()
    {
        var act = () => SiengeCostCenterPostingMapper.Map([], null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
