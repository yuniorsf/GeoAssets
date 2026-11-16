namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// Page and filter parameters for <see cref="IPoleOccupancyReader.GetPageAsync"/>. Invalid
/// paging is rejected here — on construction and on every <c>with</c> change — so every adapter
/// fails the same way; values are never clamped.
/// </summary>
public sealed record PoleOccupancyQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is below 1, or <paramref name="pageSize"/> is outside
    /// 1..<see cref="MaxPageSize"/>.
    /// </exception>
    public PoleOccupancyQuery(int page = 1, int pageSize = DefaultPageSize, Guid? occupantOrganizationId = null)
    {
        Page = page;
        PageSize = pageSize;
        OccupantOrganizationId = occupantOrganizationId;
    }

    /// <summary>1-based page number.</summary>
    public int Page
    {
        get;
        init => field = EnsurePage(value);
    }

    /// <summary>Poles per page, 1..<see cref="MaxPageSize"/>.</summary>
    public int PageSize
    {
        get;
        init => field = EnsurePageSize(value);
    }

    /// <summary>
    /// Null lists every pole with occupancy on file; otherwise only the poles on which this
    /// organization has at least one entry, in any status, on a regulated slot or beyond them.
    /// </summary>
    public Guid? OccupantOrganizationId { get; init; }

    /// <summary>Number of poles before this page: <c>(Page - 1) * PageSize</c>.</summary>
    public int Skip => checked((Page - 1) * PageSize);

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
