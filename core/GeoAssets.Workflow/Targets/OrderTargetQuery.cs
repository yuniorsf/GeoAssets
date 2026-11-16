namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Filter/sort/page parameters for <see cref="IServiceOrderTargetReader.GetPageAsync"/>.
/// Invalid paging is rejected here — on construction and on every <c>with</c> change — so every
/// adapter fails the same way; values are never clamped.
/// </summary>
public sealed record OrderTargetQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is below 1, or <paramref name="pageSize"/> is outside
    /// 1..<see cref="MaxPageSize"/>.
    /// </exception>
    public OrderTargetQuery(
        string orderId,
        int page = 1,
        int pageSize = DefaultPageSize,
        string? sortBy = null,
        bool descending = false,
        string? status = null,
        OrderTargetConflictLevel? conflictLevel = null)
    {
        OrderId = orderId;
        Page = page;
        PageSize = pageSize;
        SortBy = sortBy;
        Descending = descending;
        Status = status;
        ConflictLevel = conflictLevel;
    }

    /// <summary>The order whose targets are listed. Never blank.</summary>
    public string OrderId
    {
        get;
        init => field = EnsureOrderId(value);
    }

    /// <summary>1-based page number.</summary>
    public int Page
    {
        get;
        init => field = EnsurePage(value);
    }

    /// <summary>Items per page, 1..<see cref="MaxPageSize"/>.</summary>
    public int PageSize
    {
        get;
        init => field = EnsurePageSize(value);
    }

    /// <summary>
    /// One of the <see cref="OrderTargetSortKeys"/>. Null or an unrecognized value sorts by
    /// feature id (see <see cref="OrderTargetSortKeys.Resolve"/>) — never an error.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>Reverses the direction of the sort key; the id tiebreak stays ascending.</summary>
    public bool Descending { get; init; }

    /// <summary>Null matches any status; otherwise an exact <see cref="OrderTargetStatus"/> key.</summary>
    public string? Status { get; init; }

    /// <summary>Null matches any conflict level.</summary>
    public OrderTargetConflictLevel? ConflictLevel { get; init; }

    /// <summary>Number of items before this page: <c>(Page - 1) * PageSize</c>.</summary>
    public int Skip => checked((Page - 1) * PageSize);

    private static string EnsureOrderId(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return orderId;
    }

    private static int EnsurePage(int page)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return page;
    }

    private static int EnsurePageSize(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);
        return pageSize;
    }
}
