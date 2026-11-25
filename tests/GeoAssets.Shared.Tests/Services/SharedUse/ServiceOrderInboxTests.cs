using FluentAssertions;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.SharedUse;

/// <summary>
/// <see cref="ServiceOrderInbox"/> (XD01-316): the order list of an organization that also
/// receives orders dispatched to it by other organizations.
/// </summary>
public sealed class ServiceOrderInboxTests
{
    private static readonly Guid Own = new("a0a0a0a0-0000-4000-8000-000000000316");
    private static readonly Guid Other = new("b0b0b0b0-0000-4000-8000-000000000316");

    private static WorkflowPrincipal Principal(string? organizationId) => new("user-1", organizationId, [], [], []);

    private static ServiceOrder Order(string id, Guid organizationId, params OrderDispatch[] dispatches) =>
        new() { Id = id, Title = id, OrganizationId = organizationId, Dispatches = [.. dispatches] };

    private static OrderDispatch To(string targetId, DispatchTargetType targetType) =>
        new(targetId, targetType, "dispatcher", new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void OrganizationOf_IsThePrincipalsOrganization_InCanonicalForm()
    {
        ServiceOrderInbox.OrganizationOf(Principal(Own.ToString("D").ToUpperInvariant())).Should().Be(Own.ToString("D"));
        ServiceOrderInbox.OrganizationOf(Principal(Own.ToString("N"))).Should().Be(Own.ToString("D"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void OrganizationOf_APrincipalWithNoRealOrganization_HasNoInbox(string? organizationId) =>
        ServiceOrderInbox.OrganizationOf(Principal(organizationId)).Should().BeNull();

    [Fact]
    public void OrganizationOf_NoPrincipal_HasNoInbox() =>
        ServiceOrderInbox.OrganizationOf(null).Should().BeNull();

    [Fact]
    public void Merge_AddsTheReceivedOrdersAfterTheOwn_AndListsAnOrderInBothOnce()
    {
        var a = Order("a", Own);
        var b = Order("b", Own);
        var received = Order("r", Other);
        var bAgain = Order("b", Own);

        var merged = ServiceOrderInbox.Merge([a, b], [received, bAgain]);

        merged.Should().Equal(a, b, received);
        merged[1].Should().BeSameAs(b, "the copy from the ordinary list is the one kept");
    }

    [Fact]
    public void Merge_OfNothing_IsEmpty_AndNullListsThrow()
    {
        ServiceOrderInbox.Merge([], []).Should().BeEmpty();
        ((Action)(() => ServiceOrderInbox.Merge(null!, []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ServiceOrderInbox.Merge([], null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsReceived_IsTrueForAnOrderOfAnotherOrganizationDispatchedToTheViewersOrganization_InAnySpelling()
    {
        ServiceOrderInbox.IsReceived(Order("r", Other, To(Own.ToString("D"), DispatchTargetType.Organization)), Principal(Own.ToString())).Should().BeTrue();
        ServiceOrderInbox.IsReceived(Order("r", Other, To(Own.ToString("D").ToUpperInvariant(), DispatchTargetType.Organization)), Principal(Own.ToString("N"))).Should().BeTrue();
    }

    [Fact]
    public void IsReceived_IsTrueForAnOrderOfAnotherOrganizationDispatchedToTheViewer()
    {
        ServiceOrderInbox.IsReceived(Order("r", Other, To("user-1", DispatchTargetType.User)), Principal(Own.ToString())).Should().BeTrue();
        ServiceOrderInbox.IsReceived(Order("r", Other, To("USER-1", DispatchTargetType.User)), Principal(Own.ToString())).Should().BeFalse();
    }

    [Fact]
    public void IsReceived_IsFalse_ForAnOrderOfAnotherOrganizationReachedWithoutADispatchToTheViewer()
    {
        ServiceOrderInbox.IsReceived(Order("granted", Other), Principal(Own.ToString())).Should().BeFalse("a grant alone is not a dispatch");
        ServiceOrderInbox.IsReceived(Order("elsewhere", Other, To(Guid.NewGuid().ToString(), DispatchTargetType.Organization)), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("bad-id", Other, To("not-a-guid", DispatchTargetType.Organization)), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("other-user", Other, To("user-2", DispatchTargetType.User)), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("group", Other, To(Own.ToString(), DispatchTargetType.Group)), Principal(Own.ToString())).Should().BeFalse();
    }

    [Fact]
    public void IsReceived_IsFalseForTheViewersOwnOrder_EvenWhenDispatchedInsideTheOrganization()
    {
        ServiceOrderInbox.IsReceived(Order("a", Own), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("a", Own, To(Own.ToString(), DispatchTargetType.Organization)), Principal(Own.ToString().ToUpperInvariant())).Should().BeFalse();
    }

    [Fact]
    public void IsReceived_IsFalse_ForAnOrderWithNoOrganization_ForAViewerWithNone_AndANullOrderThrows()
    {
        var dispatched = To(Own.ToString(), DispatchTargetType.Organization);
        ServiceOrderInbox.IsReceived(Order("legacy", Guid.Empty, dispatched), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("r", Other, dispatched), Principal(null)).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("r", Other, dispatched), null).Should().BeFalse();
        ((Action)(() => ServiceOrderInbox.IsReceived(null!, Principal(Own.ToString())))).Should().Throw<ArgumentNullException>();
    }
}
