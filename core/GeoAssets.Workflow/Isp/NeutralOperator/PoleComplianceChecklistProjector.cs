using System.Text.Json;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Projects a validated <see cref="PoleCensusOrderTypes.PoleCensusInspectionId"/> order's
/// <see cref="PoleSharingComplianceChecklistSchema"/>-conforming attributes onto the pole's own
/// <c>geo_entity</c> record and attachment-slot bookkeeping (XD01-259).
///
/// Callers are expected to have already run the order through
/// <see cref="ServiceOrderAttributeValidator.EnsureValid"/> (which
/// <see cref="ValidatingServiceOrderRepository"/> does automatically on every write for an
/// <see cref="OrderType"/> carrying <see cref="OrderType.AttributesSchemaJson"/>) — this type does
/// not re-validate, it only parses and projects what schema validation already guaranteed is
/// present and well-formed.
///
/// <see cref="PoleAttachmentSlotMatrix"/> (XD01-258) has no EFCore/repository layer yet — the same
/// gap XD01-266's tower-attachment catalog and XD01-271's GIS export both independently found and
/// flagged — so the occupant array's "persistence" is the validated, constructed
/// <see cref="PoleAttachmentSlotMatrix"/> this method returns; a future caller (whoever adds that
/// persistence layer, or a REST endpoint for this order type) is responsible for actually writing
/// it to a store. The scalar/structured fields (<see cref="PoleSharingComplianceChecklistSchema.StructuralConditionKey"/>
/// etc.), by contrast, are written directly onto <paramref name="order"/>'s target
/// <c>GeoFeature.Properties.CustomAttributes</c> — a real, already-persistable field on an
/// existing, already-persisted aggregate — so that half of this ticket's "persists against the
/// pole's geo_entity" acceptance criterion is a genuine write, not a placeholder.
/// </summary>
public static class PoleComplianceChecklistProjector
{
    /// <summary>
    /// Projects <paramref name="order"/>'s checklist attributes onto its single target pole
    /// feature's <c>CustomAttributes</c>, and returns the occupant array as a freshly built
    /// <see cref="PoleAttachmentSlotMatrix"/>. Throws <see cref="InvalidOperationException"/> if
    /// the order does not target exactly one feature (a pole census inspection is always
    /// single-pole, unlike a batch/area order).
    /// </summary>
    public static PoleAttachmentSlotMatrix Project(IServiceOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Features.Count != 1)
            throw new InvalidOperationException(
                $"Pole census inspection order '{order.Id}' must target exactly one pole feature; found {order.Features.Count}.");

        var pole = order.Features[0];
        var attributes = order.Attributes;

        pole.Properties.CustomAttributes[PoleSharingComplianceChecklistSchema.StructuralConditionKey] =
            attributes[PoleSharingComplianceChecklistSchema.StructuralConditionKey];
        pole.Properties.CustomAttributes[PoleSharingComplianceChecklistSchema.Band500MmCompliantKey] =
            attributes[PoleSharingComplianceChecklistSchema.Band500MmCompliantKey];
        pole.Properties.CustomAttributes[PoleSharingComplianceChecklistSchema.ClearanceMeasurementsKey] =
            attributes[PoleSharingComplianceChecklistSchema.ClearanceMeasurementsKey];
        pole.Properties.CustomAttributes[PoleSharingComplianceChecklistSchema.UnauthorizedCableDetectedKey] =
            attributes[PoleSharingComplianceChecklistSchema.UnauthorizedCableDetectedKey];

        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = pole.Id };

        using var occupantsDocument = JsonDocument.Parse(attributes[PoleSharingComplianceChecklistSchema.OccupantsKey]);
        foreach (var occupant in occupantsDocument.RootElement.EnumerateArray())
        {
            var slot = new PoleAttachmentSlot
            {
                SlotIndex = occupant.GetProperty("slotIndex").GetInt32(),
                Status = Enum.Parse<PoleAttachmentOccupantStatus>(occupant.GetProperty("status").GetString()!),
            };

            if (occupant.TryGetProperty("operatorCnpj", out var cnpj) && cnpj.ValueKind == JsonValueKind.String)
                slot.OperatorCnpj = cnpj.GetString();

            if (occupant.TryGetProperty("cableLoadDaN", out var load) && load.ValueKind == JsonValueKind.Number)
                slot.CableLoadDaN = load.GetDecimal();

            matrix.AddSlot(slot);
        }

        return matrix;
    }
}
