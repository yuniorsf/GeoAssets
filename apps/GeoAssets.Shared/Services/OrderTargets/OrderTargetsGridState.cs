using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The view state of an order's paged targets grid (XD01-312): page, page size, sort and filters,
/// and the <see cref="OrderTargetQuery"/> it reads with. Immutable; every change of sort, filter or
/// page size returns to the first page, so a page number never outlives the result it counted.
/// Pages are 1-based, as the query's.
/// </summary>
public sealed record OrderTargetsGridState
{
    /// <summary>The page sizes the grid offers.</summary>
    public static IReadOnlyList<int> PageSizeOptions { get; } = [10, 25, 50, 100];

    /// <summary>The grid's initial state: first page, 25 rows, by feature id ascending, no filter.</summary>
    public static OrderTargetsGridState Default { get; } = new();

    public int Page { get; private init; } = 1;

    public int PageSize { get; private init; } = 25;

    /// <summary>One of the <see cref="OrderTargetSortKeys"/>.</summary>
    public string SortBy { get; private init; } = OrderTargetSortKeys.FeatureId;

    public bool Descending { get; private init; }

    /// <summary>One of the <see cref="OrderTargetStatus"/> keys, or null for every status.</summary>
    public string? Status { get; private init; }

    /// <summary>A conflict level, or null for every level.</summary>
    public OrderTargetConflictLevel? Conflict { get; private init; }

    /// <summary>The read for <paramref name="orderId"/>. The store adds the id tiebreak, so the order is total.</summary>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    public OrderTargetQuery ToQuery(string orderId) =>
        new(orderId, Page, PageSize, SortBy, Descending, Status, Conflict);

    /// <summary>
    /// Sorting by the header that is already the sort key reverses the direction; another header
    /// sorts by its key ascending. An unknown key sorts by feature id, as the store would.
    /// </summary>
    public OrderTargetsGridState WithSort(string sortBy)
    {
        var key = OrderTargetSortKeys.Resolve(sortBy);
        var descending = string.Equals(key, SortBy, StringComparison.Ordinal) && !Descending;
        return this with { SortBy = key, Descending = descending, Page = 1 };
    }

    /// <summary>Filters by a status key; null, blank or an undefined key shows every status.</summary>
    public OrderTargetsGridState WithStatus(string? status) =>
        this with { Status = OrderTargetStatus.IsDefined(status) ? status : null, Page = 1 };

    /// <summary>Filters by a conflict level; null or an undefined value shows every level.</summary>
    public OrderTargetsGridState WithConflict(OrderTargetConflictLevel? conflict) =>
        this with { Conflict = conflict is { } level && Enum.IsDefined(level) ? level : null, Page = 1 };

    /// <summary>Changes the page size; a size the grid does not offer keeps the current one.</summary>
    public OrderTargetsGridState WithPageSize(int pageSize) =>
        PageSizeOptions.Contains(pageSize) ? this with { PageSize = pageSize, Page = 1 } : this;

    /// <summary>Moves to <paramref name="page"/>, brought into <c>[1, total pages]</c>.</summary>
    public OrderTargetsGridState WithPage(int page, int totalCount) =>
        this with { Page = OrderTargetPaging.Clamp(page, totalCount, PageSize) };

    /// <summary>The <c>aria-sort</c> value of the header sorting by <paramref name="sortBy"/>.</summary>
    public string AriaSort(string sortBy)
    {
        if (!string.Equals(sortBy, SortBy, StringComparison.Ordinal)) return "none";

        return Descending ? "descending" : "ascending";
    }
}
