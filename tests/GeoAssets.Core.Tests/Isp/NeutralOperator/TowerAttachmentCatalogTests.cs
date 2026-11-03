using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator;

public class TowerAttachmentCatalogTests
{
    private static TowerAttachmentRecord Bracket(string id, decimal elevationMeters = 10m) =>
        new() { RecordId = id, Kind = TowerAttachmentKind.Bracket, ElevationMeters = elevationMeters };

    private static TowerAttachmentRecord Radome(string id, string inventoryId = "RAD-1") =>
        new() { RecordId = id, Kind = TowerAttachmentKind.Radome, InventoryIdentifier = inventoryId };

    private static TowerAttachmentRecord Dish(string id, string inventoryId = "DISH-1") =>
        new() { RecordId = id, Kind = TowerAttachmentKind.Dish, InventoryIdentifier = inventoryId };

    private static TowerAttachmentRecord PowerMeter(string id, decimal wattsDraw = 250m) =>
        new() { RecordId = id, Kind = TowerAttachmentKind.PowerMeter, MeteredPowerDrawWatts = wattsDraw };

    [Fact]
    public void AddRecord_AllFourKinds_Succeeds()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };

        catalog.AddRecord(Bracket("b1"));
        catalog.AddRecord(Radome("r1"));
        catalog.AddRecord(Dish("d1"));
        catalog.AddRecord(PowerMeter("p1"));

        catalog.Records.Should().HaveCount(4);
    }

    [Fact]
    public void AddRecord_NoSlotCeiling_AcceptsMoreThanSixRecords()
    {
        // Unlike PoleAttachmentSlotMatrix.MaxSlots, a tower has no fixed ceiling.
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };

        for (var i = 0; i < 10; i++)
            catalog.AddRecord(Bracket($"b{i}"));

        catalog.Records.Should().HaveCount(10);
    }

    [Fact]
    public void AddRecord_DuplicateRecordId_Throws()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        catalog.AddRecord(Bracket("b1"));

        var act = () => catalog.AddRecord(Radome("b1"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void AddRecord_BracketWithoutElevation_Throws()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        var record = new TowerAttachmentRecord { RecordId = "b1", Kind = TowerAttachmentKind.Bracket };

        var act = () => catalog.AddRecord(record);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ElevationMeters*");
    }

    [Fact]
    public void AddRecord_PowerMeterWithoutDraw_Throws()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        var record = new TowerAttachmentRecord { RecordId = "p1", Kind = TowerAttachmentKind.PowerMeter };

        var act = () => catalog.AddRecord(record);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MeteredPowerDrawWatts*");
    }

    [Fact]
    public void AddRecord_RadomeWithoutElevationOrDraw_Succeeds()
    {
        // Radome/Dish only require an inventory identifier, not elevation or wattage.
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };

        catalog.AddRecord(Radome("r1"));

        catalog.Records.Should().ContainSingle(r => r.RecordId == "r1");
    }

    [Fact]
    public void RemoveRecord_ExistingId_Removes()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        catalog.AddRecord(Bracket("b1"));

        catalog.RemoveRecord("b1");

        catalog.Records.Should().BeEmpty();
    }

    [Fact]
    public void RemoveRecord_UnknownId_Throws()
    {
        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };

        var act = () => catalog.RemoveRecord("missing");

        act.Should().Throw<KeyNotFoundException>();
    }
}
