using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.SharedUse;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// <see cref="ISharedUseFanOutTracer"/> on <see cref="GeoAssetsActivitySource"/> (XD01-316): one
/// <c>ServiceOrder.FanOutChild</c> span per child order. <c>EFOutboxStore</c> captures the
/// current activity when a message is staged, so the child's notification is published as a
/// continuation of that child's span.
/// </summary>
public sealed class ActivitySharedUseFanOutTracer(GeoAssetsActivitySource tracer) : ISharedUseFanOutTracer
{
    public IDisposable? BeginChild(string parentOrderId, string childOrderId, Guid occupantOrganizationId) =>
        tracer.StartFanOutChildActivity(parentOrderId, childOrderId, occupantOrganizationId.ToString("D"));
}
