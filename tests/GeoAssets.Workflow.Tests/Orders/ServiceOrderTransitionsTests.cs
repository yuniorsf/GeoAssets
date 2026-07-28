using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ServiceOrderTransitionsTests
{
    // ── Legal edges ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Cancelled)]
    public void IsValid_LegalTransition_ReturnsTrue(ServiceOrderStatus from, ServiceOrderStatus to)
    {
        ServiceOrderTransitions.IsValid(from, to).Should().BeTrue();
    }

    // ── Same-status no-op ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public void IsValid_SameStatus_ReturnsTrue(ServiceOrderStatus status)
    {
        ServiceOrderTransitions.IsValid(status, status).Should().BeTrue();
    }

    // ── Illegal edges ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Completed,  ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Completed,  ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Cancelled,  ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Cancelled,  ServiceOrderStatus.InProgress)]
    public void IsValid_IllegalTransition_ReturnsFalse(ServiceOrderStatus from, ServiceOrderStatus to)
    {
        ServiceOrderTransitions.IsValid(from, to).Should().BeFalse();
    }
}
