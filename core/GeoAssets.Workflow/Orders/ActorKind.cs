namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Distinguishes what kind of actor performed an action or holds a
/// <see cref="Rules.WorkflowPrincipal"/> — a human user, an AI agent, or an
/// automated system process. Authorization and transition logic never branch
/// on this value; it exists purely for audit/observability.
/// </summary>
public enum ActorKind
{
    Human,
    Agent,
    System
}
