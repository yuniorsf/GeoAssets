namespace GeoAssets.Workflow.Rest;

/// <summary>The paged-collection envelope of the order-target routes, as it arrives on the wire.</summary>
internal sealed record PagedEnvelope<T>(IReadOnlyList<T>? Items, int Page, int PageSize, int TotalCount);
