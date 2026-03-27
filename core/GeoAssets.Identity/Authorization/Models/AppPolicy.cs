namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Named authorization policy stored in the database.
///
/// A policy groups a set of <see cref="PolicyRequirement"/> items.
/// The <see cref="Operator"/> controls whether ALL requirements must be met (AND)
/// or ANY single requirement is sufficient (OR).
///
/// Examples:
///   "CanCreateServiceOrders"  → Operator=All: Role=FieldTechnician, Claim zone=*, Permission serviceorders:create
///   "CanViewReports"          → Operator=Any: Role=Supervisor OR Role=Administrator
/// </summary>
public sealed class AppPolicy
{
    public Guid            Id          { get; set; } = Guid.NewGuid();
    public string          Name        { get; set; } = string.Empty;
    public string          Description { get; set; } = string.Empty;

    /// <summary>
    /// How requirements are combined:
    ///   <see cref="PolicyOperator.All"/> — user must satisfy every requirement (AND).
    ///   <see cref="PolicyOperator.Any"/> — user must satisfy at least one (OR).
    /// </summary>
    public PolicyOperator  Operator    { get; set; } = PolicyOperator.All;

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<PolicyRequirement> Requirements { get; set; } = [];
}
