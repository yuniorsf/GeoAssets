namespace GeoAssets.NeutralOperator.Modules.SharedUse;

/// <summary>
/// Body of <c>POST /service-orders/{id}/shared-use/dispatch</c>.
/// </summary>
/// <param name="RowVersion">
/// Required: the order's concurrency token as the caller last read it (base64 on the wire, as
/// the order and the dispatch plan carry it). An empty value states no precondition.
/// </param>
public sealed record DispatchSharedUseOrderRequest(byte[]? RowVersion);
