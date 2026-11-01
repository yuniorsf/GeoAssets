using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// The two digital APR (Análise Preliminar de Risco) safety-checklist schemas (XD01-222),
/// validated via <see cref="ServiceOrderAttributeValidator"/>'s existing JsonSchema.Net pipeline
/// against <see cref="IServiceOrder.Attributes"/> — the crew's mobile submission is simply the
/// order's own attribute set, so no new submission/storage mechanism is needed.
///
/// <see cref="ElectricalAprSchema"/>'s boolean gates (NR-10 electrical safety, NR-35 work at
/// height — including <c>nr35_pole_structural_integrity</c> and
/// <c>nr35_weatherConditionsAcceptable</c>, XD01-245) and <see cref="ConfinedSpaceNr33Schema"/>'s
/// gas-range bounds (NR-33 confined space) are a representative, structurally-correct starting
/// checklist — the gate *names* and *mechanics* (every safety-critical boolean is
/// <c>const: true</c>, so an explicit "false" or "not yet confirmed" omission both fail
/// validation the same way; the gas bounds are a real, commonly cited atmospheric-testing range;
/// <c>aprSignedAtUtc</c> is a required ISO-8601 UTC timestamp, so an APR submission is always
/// dated — XD01-245's "timestamped APR submission" acceptance criterion) are what this ticket
/// owns. The exact current wording and numeric thresholds in Brazil's official
/// NR-10/NR-33/NR-35 texts were not independently re-verified against the regulator's current
/// publication — review these specific values with the concessionaire's own safety/compliance
/// team before relying on them for a real crew's go/no-go decision, the same caveat
/// XD01-209/210/211's regulatory domain assemblies carry for their own unverified constants.
/// </summary>
public static class SafetyChecklistSchemas
{
    public const string ElectricalAprSchema = """
    {
      "type": "object",
      "properties": {
        "circuitDeenergized":    { "const": true },
        "lockoutTagoutApplied":  { "const": true },
        "voltageAbsenceVerified": { "const": true },
        "groundingApplied":      { "const": true },
        "ppeInspected":          { "const": true },
        "workAreaIsolated":      { "const": true },
        "fallProtectionAnchored": { "const": true },
        "riskAnalysisSignedByCrew": { "const": true },
        "nr35_pole_structural_integrity": { "const": true },
        "nr35_weatherConditionsAcceptable": { "const": true },
        "aprSignedAtUtc": { "type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?(Z|[+-]\\d{2}:\\d{2})$" }
      },
      "required": [
        "circuitDeenergized",
        "lockoutTagoutApplied",
        "voltageAbsenceVerified",
        "groundingApplied",
        "ppeInspected",
        "workAreaIsolated",
        "fallProtectionAnchored",
        "riskAnalysisSignedByCrew",
        "nr35_pole_structural_integrity",
        "nr35_weatherConditionsAcceptable",
        "aprSignedAtUtc"
      ],
      "additionalProperties": false
    }
    """;

    public const string ConfinedSpaceNr33Schema = """
    {
      "type": "object",
      "properties": {
        "entryPermitIssued":           { "const": true },
        "continuousVentilationActive": { "const": true },
        "rescuePlanInPlace":           { "const": true },
        "standbyAttendantPresent":     { "const": true },
        "communicationEquipmentVerified": { "const": true },
        "oxygenPercent":               { "type": "number", "minimum": 19.5, "maximum": 23.0 },
        "lowerExplosiveLimitPercent":  { "type": "number", "minimum": 0, "maximum": 10 },
        "carbonMonoxidePpm":           { "type": "number", "minimum": 0, "maximum": 35 },
        "hydrogenSulfidePpm":          { "type": "number", "minimum": 0, "maximum": 10 }
      },
      "required": [
        "entryPermitIssued",
        "continuousVentilationActive",
        "rescuePlanInPlace",
        "standbyAttendantPresent",
        "communicationEquipmentVerified",
        "oxygenPercent",
        "lowerExplosiveLimitPercent",
        "carbonMonoxidePpm",
        "hydrogenSulfidePpm"
      ],
      "additionalProperties": false
    }
    """;

    /// <summary>Builds a field-work <see cref="OrderType"/> gated by <see cref="ElectricalAprSchema"/>.</summary>
    public static OrderType BuildElectricalOrderType(string id, string displayName, string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(id, displayName, description, attributesSchemaJson: ElectricalAprSchema);

    /// <summary>Builds a field-work <see cref="OrderType"/> gated by <see cref="ConfinedSpaceNr33Schema"/>.</summary>
    public static OrderType BuildConfinedSpaceOrderType(string id, string displayName, string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(id, displayName, description, attributesSchemaJson: ConfinedSpaceNr33Schema);
}
