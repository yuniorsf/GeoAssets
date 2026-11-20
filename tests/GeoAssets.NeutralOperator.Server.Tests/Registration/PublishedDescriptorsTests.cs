using System.Text.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// What the host tells the kernel about its order types (XD01-382): data the kernel's registry and global target ledger accept —
/// valid descriptors, each with the target policy and terminal statuses the ledger reserves and releases by — that survives
/// the wire (the registration client's JSON options) unchanged.
/// </summary>
public sealed class PublishedDescriptorsTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(registry);
        return registry;
    }

    private static IReadOnlyList<OrderTypeDescriptor> Published() => NeutralOperatorModule.PublishedDescriptors(Registry());

    [Fact]
    public void PublishedDescriptors_PassTheKernelsValidation()
    {
        OrderTypeDescriptorValidation.FindProblem(NeutralOperatorModule.ModuleId, Published()).Should().BeNull();
    }

    [Fact]
    public void PublishedDescriptors_DescribeEveryOrderTypeOfTheVertical_InIdOrder()
    {
        var own = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(own);

        Published().Select(d => d.OrderTypeId).Should().Equal(own.All.Select(t => t.Id).Order(StringComparer.Ordinal));
        Published().Select(d => d.OrderTypeId).Should().Contain([SharedUseOrderTypes.PoleWorkId, SharedUseOrderTypes.OccupantNoticeId]);
        Published().Should().OnlyContain(d => string.Equals(d.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));
    }

    [Fact]
    public void PublishedDescriptors_CarryTheTargetPolicyOfEachOrderType_SoTheLedgerCanReserveItsTargets()
    {
        var registry = Registry();

        foreach (var descriptor in Published())
        {
            var orderType = registry.Get(descriptor.OrderTypeId);
            if (orderType.TargetPolicy is null) descriptor.TargetPolicy.Should().BeNull(descriptor.OrderTypeId);
            else JsonSerializer.Serialize(descriptor.TargetPolicy).Should().Be(JsonSerializer.Serialize(orderType.TargetPolicy), descriptor.OrderTypeId);
        }

        Published().Single(d => string.Equals(d.OrderTypeId, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal)).TargetPolicy!.FanOut.Should().NotBeNull();
        Published().Count(d => d.TargetPolicy is not null).Should().BeGreaterThan(0);
    }

    [Fact]
    public void PublishedDescriptors_CarryTheTerminalStatusesOfEachOrderType_SoTheLedgerKnowsWhenToRelease()
    {
        var registry = Registry();

        foreach (var descriptor in Published())
        {
            var orderType = registry.Get(descriptor.OrderTypeId);
            descriptor.TerminalStatuses.Should().BeEquivalentTo(
                orderType.States.Select(s => s.Key).Where(status => ServiceOrderTransitions.IsTerminalState(orderType, status)), descriptor.OrderTypeId);
            descriptor.TerminalStatuses.Should().NotBeEmpty(descriptor.OrderTypeId);
        }

        Published().Single(d => string.Equals(d.OrderTypeId, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal)).TerminalStatuses
            .Should().BeEquivalentTo(SharedUseOrderTypes.ApprovedStateKey, ServiceOrderStatus.Cancelled)
            .And.NotContain(ServiceOrderStatus.Completed, "completed pole work still awaits its approval");
        Published().Single(d => string.Equals(d.OrderTypeId, SharedUseOrderTypes.OccupantNoticeId, StringComparison.Ordinal)).TerminalStatuses
            .Should().Contain([ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled]);
    }

    [Fact]
    public void PublishedDescriptors_KeepTheSharedUseFeatureAction_OnThePoleWorkOrderType()
    {
        var descriptor = Published().Single(d => string.Equals(d.OrderTypeId, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal));

        descriptor.FeatureActions.Should().ContainSingle().Which.Should().Be(new FeatureActionDescriptor(
            OccupancyAssetTypeRoles.OccupiableSupport, "create", "featureActions.createSharedUseOrder", SharedUseOrderTypes.PoleWorkId, "/api/v/neutral-operator/workflow/service-orders"));
        descriptor.DisplayNameKey.Should().Be("orderTypes.sharedUsePoleWork");
        Published().Where(d => !string.Equals(d.OrderTypeId, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal)).Should().OnlyContain(d => d.FeatureActions.Count == 0);
    }

    [Fact]
    public void PublishedDescriptors_RoundTripThroughTheWireJson_Unchanged()
    {
        var published = Published();

        var read = JsonSerializer.Deserialize<List<OrderTypeDescriptor>>(JsonSerializer.Serialize(published, Wire), Wire)!;

        read.Should().HaveSameCount(published);
        foreach (var (sent, received) in published.Zip(read))
            received.SameAs(sent).Should().BeTrue($"'{sent.OrderTypeId}' must arrive as it left");
        OrderTypeDescriptorValidation.FindProblem(NeutralOperatorModule.ModuleId, read).Should().BeNull("the kernel validates what it received");
    }

    [Fact]
    public void PublishedDescriptors_TakeTheHostsOwnRegistrationOfAType_OverTheModulesDefault()
    {
        var registry = Registry();
        var pole = registry.Get(SharedUseOrderTypes.PoleWorkId);
        registry.Register(new OrderType
        {
            Id = pole.Id,
            DisplayName = pole.DisplayName,
            CreationPolicies = pole.CreationPolicies,
            InitialStateKey = pole.InitialStateKey,
            RecipientAccess = pole.RecipientAccess,
            States = pole.States,
            Transitions = pole.Transitions,
            Capabilities = pole.Capabilities,
            TargetPolicy = pole.TargetPolicy! with { SelectionLimit = 7 },
        });

        NeutralOperatorModule.PublishedDescriptors(registry).Single(d => string.Equals(d.OrderTypeId, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal))
            .TargetPolicy!.SelectionLimit.Should().Be(7, "an overlay of the registry (a type stored in this host's database) is what the ledger must enforce");
    }
}
