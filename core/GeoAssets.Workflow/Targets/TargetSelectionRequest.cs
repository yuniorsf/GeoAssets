namespace GeoAssets.Workflow.Targets;

/// <summary>
/// One run of a selection strategy for an order: the input of
/// <see cref="IOrderTargetSelectionService.PreviewAsync"/> and
/// <see cref="IOrderTargetSelectionService.ApplyAsync"/>.
/// <see cref="Page"/> and <see cref="PageSize"/> page the previewed targets only (1-based,
/// 1..<see cref="OrderTargetQuery.MaxPageSize"/>); an out-of-range value throws
/// <see cref="ArgumentOutOfRangeException"/> here, on construction and on <c>with</c>, and is
/// never clamped.
/// </summary>
public sealed record TargetSelectionRequest
{
    public TargetSelectionRequest(
        string orderId,
        string strategyId,
        IReadOnlyDictionary<string, object> parameters,
        int page = 1,
        int pageSize = OrderTargetQuery.DefaultPageSize,
        bool acceptTruncation = false)
    {
        OrderId = orderId;
        StrategyId = strategyId;
        Parameters = parameters;
        Page = page;
        PageSize = pageSize;
        AcceptTruncation = acceptTruncation;
    }

    /// <summary>The order the selection is for.</summary>
    public string OrderId { get; init; }

    /// <summary>The selection strategy to run, e.g. <c>polygon-fence</c> or <c>identifier-list</c>.</summary>
    public string StrategyId { get; init; }

    /// <summary>
    /// The strategy's parameters as supplied by the caller. The service overrides the asset-type
    /// filter, the result ceiling and the organization; nothing here can widen them.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>1-based page of the previewed targets.</summary>
    public int Page
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    }

    /// <summary>Size of a preview page, 1..<see cref="OrderTargetQuery.MaxPageSize"/>.</summary>
    public int PageSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, OrderTargetQuery.MaxPageSize);
            field = value;
        }
    }

    /// <summary>
    /// Apply only: when the selection does not fit under the order type's selection limit, add
    /// the part that fits (lowest feature ids first) instead of rejecting the whole selection.
    /// </summary>
    public bool AcceptTruncation { get; init; }
}
