namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The <c>PoleSharingComplianceChecklist</c> mobile audit submission schema (XD01-259),
/// validated via <see cref="Orders.ServiceOrderAttributeValidator"/>'s existing JsonSchema.Net
/// pipeline against <see cref="Orders.IServiceOrder.Attributes"/> — same "the submission is just
/// the order's own attribute set" convention <see cref="FieldWork.SafetyChecklistSchemas"/>
/// already established for the digital APR checklists (XD01-222).
///
/// <see cref="Orders.ServiceOrderAttributeValidator"/>'s string-dictionary coercion (each
/// attribute value is tried as JSON first, falling back to a bare JSON string) means a nested
/// object/array field — <c>clearanceMeasurements</c>, <c>occupants</c> — must be submitted as a
/// JSON-encoded string value, not a literal nested structure; a scalar field (<c>structuralCondition</c>,
/// <c>band500mmCompliant</c>, <c>unauthorizedCableDetected</c>) is submitted as plain text/boolean/number
/// text exactly like every other <see cref="Orders.OrderType.AttributesSchemaJson"/>-gated order type
/// in this codebase.
///
/// <b>The field list below (structural condition, 500mm band compliance, clearance measurements,
/// occupant array, unauthorized-cable flag) is taken verbatim from the ticket; the specific enum
/// values and numeric bounds are this implementation's own reasonable invention grounded in
/// plausible ABNT/ANEEL pole-sharing norms — no original source JSON Schema document exists in
/// this repo to copy from.</b> Review these exact values with whoever owns the real
/// PoleSharingComplianceChecklist spec before relying on them for a real inspector's submission —
/// the same caveat <see cref="FieldWork.SafetyChecklistSchemas"/> and
/// <c>LevantamentoWorkbookRowParser</c>/<c>FiscalizacaoWorkbookRowParser</c> already carry for
/// their own unverified constants.
/// </summary>
public static class PoleSharingComplianceChecklistSchema
{
    /// <summary>Attribute key holding the pole's overall structural condition
    /// (<c>Good</c>/<c>Fair</c>/<c>Poor</c>/<c>Critical</c>).</summary>
    public const string StructuralConditionKey = "structuralCondition";

    /// <summary>Attribute key holding whether the 500mm telecom band separation from the power
    /// conductors is compliant (boolean).</summary>
    public const string Band500MmCompliantKey = "band500mmCompliant";

    /// <summary>Attribute key holding the JSON-encoded clearance-measurements object
    /// (<c>groundToLowestCableMeters</c>, <c>powerToCommunicationBandMeters</c>).</summary>
    public const string ClearanceMeasurementsKey = "clearanceMeasurements";

    /// <summary>Attribute key holding the JSON-encoded occupant array (one entry per physically
    /// observed attachment, up to <see cref="GeoAssets.Core.Isp.NeutralOperator.PoleAttachmentSlotMatrix.MaxSlots"/>).</summary>
    public const string OccupantsKey = "occupants";

    /// <summary>Attribute key holding whether the inspector flagged an unauthorized
    /// ("clandestine") cable on this pole (boolean) — a coarse, inspector-asserted summary flag,
    /// independent of (and not a substitute for) deriving the same conclusion from each
    /// individual occupant's <see cref="GeoAssets.Core.Isp.NeutralOperator.PoleAttachmentOccupantStatus"/>.</summary>
    public const string UnauthorizedCableDetectedKey = "unauthorizedCableDetected";

    public const string Schema = """
    {
      "type": "object",
      "properties": {
        "structuralCondition": {
          "type": "string",
          "enum": ["Good", "Fair", "Poor", "Critical"]
        },
        "band500mmCompliant": { "type": "boolean" },
        "clearanceMeasurements": {
          "type": "object",
          "properties": {
            "groundToLowestCableMeters":        { "type": "number", "minimum": 0, "maximum": 15 },
            "powerToCommunicationBandMeters":   { "type": "number", "minimum": 0, "maximum": 2 }
          },
          "required": ["groundToLowestCableMeters", "powerToCommunicationBandMeters"],
          "additionalProperties": false
        },
        "occupants": {
          "type": "array",
          "minItems": 0,
          "maxItems": 6,
          "items": {
            "type": "object",
            "properties": {
              "slotIndex": { "type": "integer", "minimum": 1, "maximum": 6 },
              "status": {
                "type": "string",
                "enum": ["ActiveReservation", "AuthorizedContracted", "AbandonedDeadDrop", "ClandestineUnauthorized"]
              },
              "operatorCnpj": { "type": "string" }
            },
            "required": ["slotIndex", "status"],
            "additionalProperties": false
          }
        },
        "unauthorizedCableDetected": { "type": "boolean" }
      },
      "required": [
        "structuralCondition",
        "band500mmCompliant",
        "clearanceMeasurements",
        "occupants",
        "unauthorizedCableDetected"
      ],
      "additionalProperties": false
    }
    """;
}
