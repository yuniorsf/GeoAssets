using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The pole census inspection field-order type (XD01-259) — a crew visits a pole and submits a
/// <see cref="PoleSharingComplianceChecklistSchema"/>-conforming attribute set. Built on
/// <see cref="FieldWorkWorkflowGraph"/>, the same 12-state Draft→...→Inspected graph the digital
/// APR checklists (<see cref="SafetyChecklistSchemas"/>) already use — a pole census is itself a
/// field-work order, not a new workflow shape.
/// </summary>
public static class PoleCensusOrderTypes
{
    public const string PoleCensusInspectionId = "pole-census-inspection";

    public static OrderType BuildPoleCensusInspection(string displayName = "Pole Census Inspection", string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(
            PoleCensusInspectionId,
            displayName,
            description,
            attributesSchemaJson: PoleSharingComplianceChecklistSchema.Schema);
}
