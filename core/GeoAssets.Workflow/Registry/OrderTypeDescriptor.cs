namespace GeoAssets.Workflow.Registry;

/// <summary>
/// What a vertical publishes about one of its order types (XD01-369, the order-type chassis contract): data, never code —
/// the kernel resolves its <see cref="FeatureActions"/> against asset-type roles without running the vertical.
/// </summary>
public sealed record OrderTypeDescriptor
{
    public required string VerticalId { get; init; }
    public required string OrderTypeId { get; init; }
    public required string DisplayNameKey { get; init; }

    /// <summary>The base route of the vertical's order API, e.g. <c>/api/workflow/service-orders</c>.</summary>
    public required string ApiBaseRoute { get; init; }

    public IReadOnlyList<FeatureActionDescriptor> FeatureActions { get; init => field = value ?? []; } = [];

    /// <summary>Structural equality: the same fields and the same feature actions, in order.</summary>
    public bool SameAs(OrderTypeDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(VerticalId, other.VerticalId, StringComparison.Ordinal)
            && string.Equals(OrderTypeId, other.OrderTypeId, StringComparison.Ordinal)
            && string.Equals(DisplayNameKey, other.DisplayNameKey, StringComparison.Ordinal)
            && string.Equals(ApiBaseRoute, other.ApiBaseRoute, StringComparison.Ordinal)
            && FeatureActions.SequenceEqual(other.FeatureActions);
    }
}
