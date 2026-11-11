using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Dispatch;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

[Collection("AgentObservability")]
public class FeederClusterAutoDispatchServiceTests
{
    private const string AgentId = "agent-dispatch-01";
    private const string ElectricalCrewId = "crew-electrical";
    private const string PlumbingCrewId = "crew-plumbing";

    private static ServiceOrderRules Rules() => new(
        roleGrants: new Dictionary<string, IReadOnlySet<OrderActionType>>(StringComparer.Ordinal)
        {
            ["AutomationAgent"] = new HashSet<OrderActionType> { OrderActionType.Dispatch },
        });

    private static ConfiguredAgentIdentityProvider AgentIdentity()
    {
        var options = new AgentIdentityOptions
        {
            Agents = { [AgentId] = new AgentIdentityDescriptor { RoleNames = ["AutomationAgent"] } },
        };
        return new ConfiguredAgentIdentityProvider(Options.Create(options));
    }

    private static TestAssetProvider SeededAssetProvider()
    {
        var provider = new TestAssetProvider();

        provider.Add(new GeoFeature
        {
            Id = ElectricalCrewId,
            Geometry = new GeoPoint(-46.00, -23.00),
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = FeederClusterDispatchTypes.RoutineCrewAssetTypeId,
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [FeederClusterDispatchTypes.CrewSkillsAttributeKey] = "electrical",
                },
            },
        });
        provider.Add(new GeoFeature
        {
            Id = PlumbingCrewId,
            Geometry = new GeoPoint(-46.00, -23.00),
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = FeederClusterDispatchTypes.RoutineCrewAssetTypeId,
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [FeederClusterDispatchTypes.CrewSkillsAttributeKey] = "plumbing",
                },
            },
        });
        provider.Add(new GeoFeature { Id = "pole-electrical", Geometry = new GeoPoint(-46.01, -23.01), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        provider.Add(new GeoFeature { Id = "pole-no-match", Geometry = new GeoPoint(-46.02, -23.02), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        return provider;
    }

    private static ServiceOrder DraftOrder(string id, GeoFeature feature, string? requiredSkills = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (requiredSkills is not null)
            attributes[FeederClusterDispatchTypes.OrderRequiredSkillsAttributeKey] = requiredSkills;

        return new ServiceOrder
        {
            Id = id,
            Title = $"Trouble call {id}",
            OrderTypeId = "trouble-call",
            Status = ServiceOrderStatus.Draft,
            // SnapshottingServiceOrderRepository.Clone only preserves the hydrated Features
            // list, not FeatureIds, so the feature must be attached here, not just referenced
            // by ID, for the dispatcher to resolve a location after a repository round-trip.
            Features = [feature],
            Attributes = attributes,
        };
    }

    [Fact]
    public async Task DispatchOpenOrdersAsync_OnlyDispatchesToACrewMeetingTheDocumentedSkillRequirement()
    {
        var provider = SeededAssetProvider();
        var repository = new ValidatingServiceOrderRepository(new SnapshottingServiceOrderRepository());
        var order = DraftOrder("order-electrical", provider.GetById("pole-electrical")!, requiredSkills: "electrical");
        await repository.AddAsync(order);

        var service = new FeederClusterAutoDispatchService(
            provider, repository, new ClarkeWrightVrptwRoutingEngine(), Rules(), AgentIdentity(),
            TimeProvider.System, TestObservability.Tracer, NullLoggerFactory.Instance);

        var result = await service.DispatchOpenOrdersAsync(AgentId);

        result.DispatchedOrderIds.Should().ContainSingle().Which.Should().Be("order-electrical");
        var persisted = await repository.GetByIdAsync("order-electrical");
        persisted!.Status.Should().Be(ServiceOrderStatus.Pending);
        persisted.Dispatches.Should().ContainSingle(d => d.TargetId == ElectricalCrewId);
    }

    [Fact]
    public async Task DispatchOpenOrdersAsync_NoCrewMeetsTheSkillRequirement_LeavesOrderUnassignedAndInDraft()
    {
        var provider = SeededAssetProvider();
        var repository = new ValidatingServiceOrderRepository(new SnapshottingServiceOrderRepository());
        var order = DraftOrder("order-unmatched", provider.GetById("pole-no-match")!, requiredSkills: "hvac");
        await repository.AddAsync(order);

        var service = new FeederClusterAutoDispatchService(
            provider, repository, new ClarkeWrightVrptwRoutingEngine(), Rules(), AgentIdentity(),
            TimeProvider.System, TestObservability.Tracer, NullLoggerFactory.Instance);

        var result = await service.DispatchOpenOrdersAsync(AgentId);

        result.DispatchedOrderIds.Should().BeEmpty();
        result.UnassignedOrderIds.Should().ContainSingle().Which.Should().Be("order-unmatched");
        var persisted = await repository.GetByIdAsync("order-unmatched");
        persisted!.Status.Should().Be(ServiceOrderStatus.Draft);
    }
}
