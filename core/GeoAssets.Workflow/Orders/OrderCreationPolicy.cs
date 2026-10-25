namespace GeoAssets.Workflow.Orders;

/// <summary>
/// A single policy gate for order creation.
/// The caller satisfies the policy if their identity matches <see cref="Value"/>
/// according to <see cref="Kind"/>.
/// </summary>
public sealed record OrderCreationPolicy(PolicyKind Kind, string Value);
