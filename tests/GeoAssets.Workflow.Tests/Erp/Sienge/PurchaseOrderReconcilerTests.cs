using FluentAssertions;
using GeoAssets.Workflow.Erp.Sienge;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sienge;

public class PurchaseOrderReconcilerTests
{
    [Fact]
    public void Reconcile_PurchasedEqualsConsumed_NoFlag()
    {
        var purchaseOrders = new[] { new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m) };
        var consumption = new[] { new FieldConsumptionRecord("cc-1", "asphalt", 100m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, toleranceFraction: 0.02m);

        flags.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_DiscrepancyWithinTolerance_NoFlag()
    {
        var purchaseOrders = new[] { new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m) };
        var consumption = new[] { new FieldConsumptionRecord("cc-1", "asphalt", 99m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, toleranceFraction: 0.02m);

        flags.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_UnderConsumptionBeyondTolerance_FlagsWithCorrectMath()
    {
        var purchaseOrders = new[] { new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m) };
        var consumption = new[] { new FieldConsumptionRecord("cc-1", "asphalt", 80m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle();
        var flag = flags[0];
        flag.CostCenterCode.Should().Be("cc-1");
        flag.MaterialCode.Should().Be("asphalt");
        flag.QuantityPurchased.Should().Be(100m);
        flag.QuantityConsumed.Should().Be(80m);
        flag.DiscrepancyQuantity.Should().Be(20m);
        flag.DiscrepancyFraction.Should().Be(0.2m);
    }

    [Fact]
    public void Reconcile_OverConsumptionBeyondTolerance_FlagsWithPositiveFraction()
    {
        var purchaseOrders = new[] { new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m) };
        var consumption = new[] { new FieldConsumptionRecord("cc-1", "asphalt", 130m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle();
        var flag = flags[0];
        flag.DiscrepancyQuantity.Should().Be(-30m);
        flag.DiscrepancyFraction.Should().Be(0.3m);
    }

    [Fact]
    public void Reconcile_NoConsumptionRecordForPurchaseOrder_TreatsConsumedAsZero()
    {
        var purchaseOrders = new[] { new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 50m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, [], toleranceFraction: 0.02m);

        flags.Should().ContainSingle();
        flags[0].QuantityConsumed.Should().Be(0m);
        flags[0].DiscrepancyQuantity.Should().Be(50m);
    }

    [Fact]
    public void Reconcile_DifferentCostCenterOrMaterial_NotGroupedTogether()
    {
        var purchaseOrders = new[]
        {
            new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m),
            new SiengePurchaseOrderRecord("po-2", "cc-2", "asphalt", 100m),
        };
        var consumption = new[] { new FieldConsumptionRecord("cc-1", "asphalt", 100m) };

        var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, toleranceFraction: 0.02m);

        flags.Should().ContainSingle();
        flags[0].CostCenterCode.Should().Be("cc-2");
    }

    [Fact]
    public void Reconcile_NullPurchaseOrders_Throws()
    {
        var act = () => PurchaseOrderReconciler.Reconcile(null!, [], 0.02m);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reconcile_NegativeTolerance_Throws()
    {
        var act = () => PurchaseOrderReconciler.Reconcile([], [], -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
