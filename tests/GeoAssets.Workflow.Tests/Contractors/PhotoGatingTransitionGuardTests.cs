using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

public class PhotoGatingTransitionGuardTests
{
    private static ServiceOrder OrderWithBoqLine(string itemCode, params (string ItemCode, string Stage, string Hash)[] evidence)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServiceOrderBoqAttributes.BoqLinesKey] = $$"""[{"itemCode":"{{itemCode}}","quantity":1}]""",
        };

        if (evidence.Length > 0)
        {
            var entries = string.Join(',', evidence.Select(e => $$"""{"itemCode":"{{e.ItemCode}}","stage":"{{e.Stage}}","evidenceHash":"{{e.Hash}}"}"""));
            attributes[ServiceOrderPhotoEvidenceAttributes.EvidenceStagesKey] = $"[{entries}]";
        }

        return new ServiceOrder { Attributes = attributes };
    }

    private static PhotoGatingRuleRegistry RegistryWithRule(string itemCode, params PhotoGatingStage[] requiredStages)
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule(itemCode, requiredStages.ToHashSet()));
        return registry;
    }

    // ── CanStartWorkAsync (Before stage, AC #1) ──────────────────────────────────

    [Fact]
    public async Task CanStartWorkAsync_NoBoqLines_IsUnrestricted()
    {
        var guard = new PhotoGatingTransitionGuard(new PhotoGatingRuleRegistry());

        (await guard.CanStartWorkAsync(new ServiceOrder())).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_BoqItemWithNoRegisteredRule_IsUnrestricted()
    {
        var guard = new PhotoGatingTransitionGuard(new PhotoGatingRuleRegistry());
        var order = OrderWithBoqLine("US-EL-014");

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_GatedItemRequiresBefore_NoEvidence_ReturnsFalse()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.Before);
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014");

        (await guard.CanStartWorkAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanStartWorkAsync_GatedItemRequiresBefore_HasBeforeEvidence_ReturnsTrue()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.Before);
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014", ("US-EL-014", "Before", "sig"));

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_GatedItemDoesNotRequireBefore_IsUnrestrictedEvenWithNoEvidence()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.After); // Before not required by this item's rule
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014");

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    // ── CanCompleteAsync (all required stages, AC #2) ────────────────────────────

    [Fact]
    public async Task CanCompleteAsync_RequiresAllThreeStages_AnyOneMissing_ReturnsFalse()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.Before, PhotoGatingStage.During, PhotoGatingStage.After);
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014", ("US-EL-014", "Before", "a"), ("US-EL-014", "During", "b")); // After missing

        (await guard.CanCompleteAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanCompleteAsync_AllRequiredStagesPresent_ReturnsTrue()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.Before, PhotoGatingStage.During, PhotoGatingStage.After);
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014",
            ("US-EL-014", "Before", "a"), ("US-EL-014", "During", "b"), ("US-EL-014", "After", "c"));

        (await guard.CanCompleteAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanCompleteAsync_ItemOnlyRequiresAfter_OnlyAfterIsChecked()
    {
        var registry = RegistryWithRule("US-EL-014", PhotoGatingStage.After);
        var guard = new PhotoGatingTransitionGuard(registry);
        var order = OrderWithBoqLine("US-EL-014", ("US-EL-014", "After", "c"));

        (await guard.CanCompleteAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanArriveAsync_AlwaysReturnsTrue()
    {
        var guard = new PhotoGatingTransitionGuard(new PhotoGatingRuleRegistry());

        (await guard.CanArriveAsync(new ServiceOrder(), new GeoAssets.Core.Models.Geometry.GeoPoint(0, 0))).Should().BeTrue();
    }
}
