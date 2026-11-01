using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Real implementation of the photo-evidence half of XD01-246: an order's BoQ lines
/// (<see cref="ServiceOrderBoqAttributes.ReadBoqLines"/>) are checked against
/// <see cref="PhotoGatingRuleRegistry"/> for which <see cref="PhotoGatingStage"/>s each selected
/// item requires, and against <see cref="ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages"/>
/// for which of those stages the order already has evidence for. An item with no registered rule
/// requires nothing (unrestricted, same convention <see cref="SafetyChecklistTransitionGuard"/>
/// already uses).
///
/// <see cref="CanStartWorkAsync"/> only requires the Before stage — AC #1's "Before photo before
/// unlocking execution" is literally the <see cref="FieldWorkStatus.OnSite"/> →
/// <see cref="FieldWorkStatus.InProgress"/> edge. <see cref="CanCompleteAsync"/> requires every
/// stage the item's rule names (Before/During/After, whichever apply) — AC #2's "any required
/// stage's evidence hash is missing" literally, and the natural place to also catch a
/// since-cleared/never-captured Before stage even though <see cref="CanStartWorkAsync"/> already
/// checked it once (attributes are mutable between transitions).
///
/// AC #1's "During photo unlocks the asset-swap form" is a mid-<see cref="FieldWorkStatus.InProgress"/>
/// UI micro-interaction, not a workflow-graph edge — there is no transition to gate it on, and no
/// such form exists in this codebase yet to gate in the first place (see XD01-295, which already
/// tracks the absent field-work execution UI this and XD01-245's checklist UI both need a host
/// for). <see cref="CanCompleteAsync"/> still enforces that During evidence exists by completion
/// time, which is what AC #2 actually requires server-side.
/// </summary>
public sealed class PhotoGatingTransitionGuard(PhotoGatingRuleRegistry ruleRegistry) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) =>
        Task.FromResult(HasRequiredEvidence(order, PhotoGatingStage.Before));

    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) =>
        Task.FromResult(HasRequiredEvidence(order, PhotoGatingStage.Before)
            && HasRequiredEvidence(order, PhotoGatingStage.During)
            && HasRequiredEvidence(order, PhotoGatingStage.After));

    private bool HasRequiredEvidence(IServiceOrder order, PhotoGatingStage stage)
    {
        ArgumentNullException.ThrowIfNull(order);

        var boqLines = ServiceOrderBoqAttributes.ReadBoqLines(order.Attributes);
        if (boqLines.Count == 0) return true;

        var capturedStages = ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(order.Attributes);

        foreach (var (itemCode, _) in boqLines)
        {
            var rule = ruleRegistry.Find(itemCode);
            if (rule is null || !rule.RequiredStages.Contains(stage)) continue;

            var hasEvidence = capturedStages.Any(e =>
                string.Equals(e.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase) && e.Stage == stage);
            if (!hasEvidence) return false;
        }

        return true;
    }
}
