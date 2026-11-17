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

    private static ServiceOrder Order(string id, Guid organizationId) => new() { Id = id, Title = id, OrganizationId = organizationId };

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
    public void IsReceived_IsTrueForAnOrderOfAnotherOrganization_AndFalseForTheViewersOwn()
    {
        ServiceOrderInbox.IsReceived(Order("r", Other), Principal(Own.ToString())).Should().BeTrue();
        ServiceOrderInbox.IsReceived(Order("a", Own), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("a", Own), Principal(Own.ToString().ToUpperInvariant())).Should().BeFalse();
    }

    [Fact]
    public void IsReceived_IsFalse_ForAnOrderWithNoOrganization_ForAViewerWithNone_AndANullOrderThrows()
    {
        ServiceOrderInbox.IsReceived(Order("legacy", Guid.Empty), Principal(Own.ToString())).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("r", Other), Principal(null)).Should().BeFalse();
        ServiceOrderInbox.IsReceived(Order("r", Other), null).Should().BeFalse();
        ((Action)(() => ServiceOrderInbox.IsReceived(null!, Principal(Own.ToString())))).Should().Throw<ArgumentNullException>();
    }
}
