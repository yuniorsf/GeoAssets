using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

public sealed class OrderTargetUsageBuilder
{
    private string _featureId = "feat-100";
    private string _orderId = "ord-200";
    private string _orderTypeId = "inspection";
    private string _orderStatus = ServiceOrderStatus.InProgress;
    private string? _parentOrderId;
    private OrderTargetExclusivity _exclusivity = OrderTargetExclusivity.None;

    public OrderTargetUsageBuilder WithFeatureId(string featureId)
    {
        _featureId = featureId;
        return this;
    }

    public OrderTargetUsageBuilder WithOrderId(string orderId)
    {
        _orderId = orderId;
        return this;
    }

    public OrderTargetUsageBuilder WithOrderTypeId(string orderTypeId)
    {
        _orderTypeId = orderTypeId;
        return this;
    }

    public OrderTargetUsageBuilder WithOrderStatus(string orderStatus)
    {
        _orderStatus = orderStatus;
        return this;
    }

    public OrderTargetUsageBuilder WithParentOrderId(string? parentOrderId)
    {
        _parentOrderId = parentOrderId;
        return this;
    }

    public OrderTargetUsageBuilder WithExclusivity(OrderTargetExclusivity exclusivity)
    {
        _exclusivity = exclusivity;
        return this;
    }

    public OrderTargetUsage Build() =>
        new(
            FeatureId: _featureId,
            OrderId: _orderId,
            OrderTypeId: _orderTypeId,
            OrderStatus: _orderStatus,
            ParentOrderId: _parentOrderId,
            Exclusivity: _exclusivity);
}
