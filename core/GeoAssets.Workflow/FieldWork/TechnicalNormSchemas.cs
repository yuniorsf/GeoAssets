using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Representative technical-norm safety/compliance checklists (XD01-251) — vegetation
/// clearance near energized lines, anomaly-code/SLA acknowledgement tracking, and civil-works
/// material/compaction certification — validated via the same
/// <see cref="ServiceOrderAttributeValidator"/> pipeline <see cref="SafetyChecklistSchemas"/>
/// already uses, against <see cref="IServiceOrder.Attributes"/>.
///
/// The actual mechanism this ticket asks for — "selecting/opening an order automatically
/// applies the correct schema with no manual configuration" and "adding a new concessionaire's
/// rule set is a configuration/schema addition, not a code change" — already exists and needed
/// no new code: <see cref="FieldWorkOrderTransitioner"/>/<see cref="ServiceOrderAttributeValidator"/>
/// resolve purely from the order's own <see cref="IServiceOrder.OrderTypeId"/> via
/// <see cref="OrderTypeRegistry.Find"/>, which is itself loadable at runtime from
/// <see cref="IOrderTypeRepository"/> (<see cref="OrderTypeRegistry.LoadFromAsync"/>) — a new
/// concessionaire's norm is an <c>AddAsync</c> call against that repository, never a C# change.
/// What this ticket actually contributes is concrete starting content for that mechanism to
/// carry.
///
/// Like <see cref="SafetyChecklistSchemas"/>'s own disclaimer: these are a representative,
/// structurally-correct starting point — the exact field names/wording/numeric thresholds a
/// real DIS-NOR-068-style vegetation-clearance norm, a GED-14186/GED-94-style anomaly-code
/// catalog, or an SGM/MOS-style material-certification spec actually require were not
/// independently re-verified against any one concessionaire's current published norm; review
/// with that concessionaire's own technical-standards team before relying on them operationally.
/// Deliberately named by technical concept, not by any one concessionaire — this assembly's own
/// domain-agnostic architecture test (<c>MessagingArchitectureTests.Invariant02</c>) forbids
/// vertical/concessionaire names in identifiers here; which concessionaire's orders use which
/// schema is runtime data (an <see cref="OrderType.Id"/> string), never a type or member name.
/// </summary>
public static class TechnicalNormSchemas
{
    public const string VegetationClearanceSchema = """
    {
      "type": "object",
      "properties": {
        "minimumClearanceMaintained": { "const": true },
        "vegetationSpeciesIdentified": { "const": true },
        "clearanceVerifiedBy": { "type": "string", "minLength": 1 }
      },
      "required": [
        "minimumClearanceMaintained",
        "vegetationSpeciesIdentified",
        "clearanceVerifiedBy"
      ],
      "additionalProperties": false
    }
    """;

    public const string AnomalySlaSchema = """
    {
      "type": "object",
      "properties": {
        "anomalyCode": { "type": "string", "minLength": 1 },
        "slaDeadlineUtc": { "type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?(Z|[+-]\\d{2}:\\d{2})$" },
        "slaAcknowledged": { "const": true }
      },
      "required": [
        "anomalyCode",
        "slaDeadlineUtc",
        "slaAcknowledged"
      ],
      "additionalProperties": false
    }
    """;

    public const string MaterialCertificationSchema = """
    {
      "type": "object",
      "properties": {
        "materialLotNumber": { "type": "string", "minLength": 1 },
        "compactionCertificateNumber": { "type": "string", "minLength": 1 },
        "compactionPercentage": { "type": "number", "minimum": 90, "maximum": 100 }
      },
      "required": [
        "materialLotNumber",
        "compactionCertificateNumber",
        "compactionPercentage"
      ],
      "additionalProperties": false
    }
    """;

    /// <summary>Builds a field-work <see cref="OrderType"/> gated by <see cref="VegetationClearanceSchema"/>.</summary>
    public static OrderType BuildVegetationClearanceOrderType(string id, string displayName, string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(id, displayName, description, attributesSchemaJson: VegetationClearanceSchema);

    /// <summary>Builds a field-work <see cref="OrderType"/> gated by <see cref="AnomalySlaSchema"/>.</summary>
    public static OrderType BuildAnomalySlaOrderType(string id, string displayName, string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(id, displayName, description, attributesSchemaJson: AnomalySlaSchema);

    /// <summary>Builds a field-work <see cref="OrderType"/> gated by <see cref="MaterialCertificationSchema"/>.</summary>
    public static OrderType BuildMaterialCertificationOrderType(string id, string displayName, string description = "") =>
        FieldWorkWorkflowGraph.BuildOrderType(id, displayName, description, attributesSchemaJson: MaterialCertificationSchema);
}
