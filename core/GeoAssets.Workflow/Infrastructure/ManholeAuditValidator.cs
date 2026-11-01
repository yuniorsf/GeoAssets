using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Infrastructure;

/// <summary>
/// Validates a <see cref="ManholeAudit.Nr33ChecklistAttributes"/> submission against
/// <see cref="SafetyChecklistSchemas.ConfinedSpaceNr33Schema"/> (XD01-265's "NR-33 checklist
/// fields are captured and required before marking the inspection complete") — reuses the exact
/// schema and <see cref="ServiceOrderAttributeValidator"/> pipeline field-work orders already
/// validate against, via a throwaway <see cref="OrderType"/> wrapper (that validator's only
/// entry point), rather than re-implementing JSON Schema evaluation for the same checklist a
/// second time.
/// </summary>
public static class ManholeAuditValidator
{
    private static readonly OrderType Nr33OrderType =
        SafetyChecklistSchemas.BuildConfinedSpaceOrderType("manhole-audit-nr33", "Manhole Audit NR-33 Checklist");

    /// <summary>Returns validation errors (empty when valid).</summary>
    public static IReadOnlyList<string> ValidateNr33Checklist(IReadOnlyDictionary<string, string> attributes) =>
        ServiceOrderAttributeValidator.Validate(Nr33OrderType, attributes);

    /// <summary>Throws <see cref="ManholeAuditValidationException"/> when invalid — the gate before marking an audit complete.</summary>
    public static void EnsureCanComplete(ManholeAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        var errors = ValidateNr33Checklist(audit.Nr33ChecklistAttributes);
        if (errors.Count > 0)
            throw new ManholeAuditValidationException(audit.ConduitFeatureId, errors);
    }
}
