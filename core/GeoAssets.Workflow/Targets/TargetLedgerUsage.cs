namespace GeoAssets.Workflow.Targets;

/// <summary>
/// An open usage found by the ledger's global search (<see cref="ITargetLedgerStore.FindOpenUsagesAsync"/>), with the
/// organization of the holding order — which decides how much of it a conflict report may show another organization.
/// </summary>
/// <param name="Usage">The usage itself.</param>
/// <param name="OrganizationId">The organization of the order holding the feature.</param>
public sealed record TargetLedgerUsage(OrderTargetUsage Usage, Guid OrganizationId);
