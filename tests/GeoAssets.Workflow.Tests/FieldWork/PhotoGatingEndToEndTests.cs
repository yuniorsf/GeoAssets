using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-246's stated ACs, proven end-to-end through the real
/// <see cref="FieldWorkOrderTransitioner"/>/<see cref="CompositeFieldWorkTransitionGuard"/> path
/// a real host would use — not just <see cref="PhotoGatingTransitionGuardTests"/>'s guard-in-isolation
/// coverage.
/// </summary>
public class PhotoGatingEndToEndTests
{
    private static readonly OrderType FieldWorkOrderType = FieldWorkWorkflowGraph.BuildOrderType("field-work", "Field Work Order");

    private static CompositeFieldWorkTransitionGuard BuildGuard(PhotoGatingRuleRegistry ruleRegistry) =>
        new(NullFieldWorkTransitionGuard.Instance, NullFieldWorkTransitionGuard.Instance, new PhotoGatingTransitionGuard(ruleRegistry));

    private static ServiceOrder OrderWithBoqLineAndEvidence(string status, string itemCode, params string[] capturedStages)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServiceOrderBoqAttributes.BoqLinesKey] = $$"""[{"itemCode":"{{itemCode}}","quantity":1}]""",
        };

        if (capturedStages.Length > 0)
        {
            var entries = string.Join(',', capturedStages.Select(s => $$"""{"itemCode":"{{itemCode}}","stage":"{{s}}","evidenceHash":"sig"}"""));
            attributes[ServiceOrderPhotoEvidenceAttributes.EvidenceStagesKey] = $"[{entries}]";
        }

        return new ServiceOrder { Status = status, OrderTypeId = FieldWorkOrderType.Id, Attributes = attributes };
    }

    [Fact]
    public async Task StartWork_GatedItemMissingBeforePhoto_IsRejected()
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule("US-EL-014", new HashSet<PhotoGatingStage> { PhotoGatingStage.Before }));
        var guard = BuildGuard(registry);
        var order = OrderWithBoqLineAndEvidence(FieldWorkStatus.OnSite, "US-EL-014");

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.OnSite);
    }

    [Fact]
    public async Task StartWork_GatedItemHasBeforePhoto_IsAccepted()
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule("US-EL-014", new HashSet<PhotoGatingStage> { PhotoGatingStage.Before }));
        var guard = BuildGuard(registry);
        var order = OrderWithBoqLineAndEvidence(FieldWorkStatus.OnSite, "US-EL-014", "Before");

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.InProgress);
    }

    [Fact]
    public async Task Complete_GatedItemMissingDuringPhoto_IsRejectedServerSideEvenWithoutAnyUi()
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule("US-EL-014",
            new HashSet<PhotoGatingStage> { PhotoGatingStage.Before, PhotoGatingStage.During, PhotoGatingStage.After }));
        var guard = BuildGuard(registry);
        // A direct API call that skips straight to Completed, with Before/After captured but During missing.
        var order = OrderWithBoqLineAndEvidence(FieldWorkStatus.InProgress, "US-EL-014", "Before", "After");

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType, guard);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.InProgress);
    }

    [Fact]
    public async Task Complete_GatedItemHasAllRequiredStages_IsAccepted()
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule("US-EL-014",
            new HashSet<PhotoGatingStage> { PhotoGatingStage.Before, PhotoGatingStage.During, PhotoGatingStage.After }));
        var guard = BuildGuard(registry);
        var order = OrderWithBoqLineAndEvidence(FieldWorkStatus.InProgress, "US-EL-014", "Before", "During", "After");

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.Completed);
    }

    [Fact]
    public async Task Complete_UngatedOrder_IsAccepted()
    {
        var guard = BuildGuard(new PhotoGatingRuleRegistry());
        var order = new ServiceOrder { Status = FieldWorkStatus.InProgress, OrderTypeId = FieldWorkOrderType.Id };

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.Completed);
    }
}
