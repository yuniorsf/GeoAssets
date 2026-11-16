using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// One bounded, organization-scoped run of a selection strategy — the input of
/// <see cref="BoundedFeatureSelector.SelectAsync"/>.
/// </summary>
/// <param name="StrategyId">The strategy to run.</param>
/// <param name="Parameters">The caller's parameters; never trusted for scope (see the selector).</param>
/// <param name="OrganizationId">The only organization whose features may be returned.</param>
/// <param name="Ceiling">The most features the run may match before it is rejected; greater than zero.</param>
public sealed record BoundedFeatureSelectionRequest(
    string StrategyId,
    IReadOnlyDictionary<string, object> Parameters,
    Guid OrganizationId,
    int Ceiling)
{
    /// <summary>
    /// Asset types the result may contain. Null leaves the caller's own <c>assetTypeIds</c>
    /// parameter as the only type filter; otherwise the caller's list can only narrow this one.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedAssetTypeIds { get; init; }

    /// <summary>
    /// For <c>identifier-list</c>: the attribute to match when the caller named none. Null means
    /// the caller must name one.
    /// </summary>
    public string? DefaultAttributeKey { get; init; }

    /// <summary>The order the selection is for, handed to strategies that read it.</summary>
    public IServiceOrder? TargetOrder { get; init; }

    /// <summary>Order store handed to strategies that inherit a parent's or children's features.</summary>
    public IServiceOrderRepository? OrderRepository { get; init; }
}
