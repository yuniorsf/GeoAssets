using FluentAssertions;
using GeoAssets.Workflow.Erp.Totvs;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Totvs;

public class MaterialConsumptionReconcilerTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reconcile_IssuedEqualsConsumed_NoFlag()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now) };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-1", 100m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_ShrinkageWithinTolerance_NoFlag()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now) };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-1", 99m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_ShrinkageBeyondTolerance_FlagsWithCorrectMath()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now) };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-1", 80m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle();
        var flag = flags[0];
        flag.MaterialCode.Should().Be("cable-1");
        flag.ServiceOrderId.Should().Be("order-1");
        flag.QuantityIssued.Should().Be(100m);
        flag.QuantityConsumed.Should().Be(80m);
        flag.ShrinkageQuantity.Should().Be(20m);
        flag.ShrinkageFraction.Should().Be(0.2m);
    }

    [Fact]
    public void Reconcile_NoMatchingConsumptionRecord_TreatsConsumedAsZero()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 50m, "order-1", Now) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, [], toleranceFraction: 0.02m);

        flags.Should().ContainSingle(f => f.QuantityConsumed == 0m && f.ShrinkageQuantity == 50m);
    }

    [Fact]
    public void Reconcile_ConsumedExceedsIssued_NoFlag()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 50m, "order-1", Now) };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-1", 60m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_DifferentServiceOrders_AreNotCrossMatched()
    {
        var issuances = new[] { new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now) };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-2", 100m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle(f => f.ServiceOrderId == "order-1" && f.QuantityConsumed == 0m);
    }

    [Fact]
    public void Reconcile_MultipleIssuancesSameKey_AreSummed()
    {
        var issuances = new[]
        {
            new WarehouseStockIssuanceRecord("cable-1", "wh-1", 60m, "order-1", Now),
            new WarehouseStockIssuanceRecord("cable-1", "wh-2", 40m, "order-1", Now),
        };
        var consumption = new[] { new FieldMaterialConsumptionRecord("cable-1", "order-1", 50m) };

        var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle(f => f.QuantityIssued == 100m && f.QuantityConsumed == 50m);
    }

    [Fact]
    public void Reconcile_NegativeTolerance_Throws()
    {
        var act = () => MaterialConsumptionReconciler.Reconcile([], [], toleranceFraction: -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
