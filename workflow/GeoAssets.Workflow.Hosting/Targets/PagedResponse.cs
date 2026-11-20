namespace GeoAssets.Workflow.Hosting.Targets;

/// <summary>
/// The envelope of every paged collection on the order-target routes: one page of
/// <paramref name="Items"/>, the 1-based <paramref name="Page"/> and <paramref name="PageSize"/>
/// that produced it, and the <paramref name="TotalCount"/> over all pages.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
