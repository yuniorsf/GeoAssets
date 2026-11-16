namespace GeoAssets.Shared.Services.Occupancy;

/// <summary>Page arithmetic of the compact order-target list (XD01-315); pages are 1-based.</summary>
public static class OrderTargetPaging
{
    /// <summary>Rows per page of the compact list: enough to pick a pole from, short enough to sit inside the order detail.</summary>
    public const int PageSize = 10;

    /// <summary>Number of pages needed for <paramref name="totalCount"/> rows; 0 when there are none.</summary>
    public static int TotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 || totalCount <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    /// <summary><paramref name="page"/> brought into <c>[1, total pages]</c> (1 when there are no rows).</summary>
    public static int Clamp(int page, int totalCount, int pageSize) =>
        Math.Clamp(page, 1, Math.Max(TotalPages(totalCount, pageSize), 1));

    public static bool HasPrevious(int page) => page > 1;

    public static bool HasNext(int page, int totalCount, int pageSize) => page < TotalPages(totalCount, pageSize);
}
