using GeoAssets.Core.Models;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// Wire shape of a paged <c>GeoAssets.IpBilling.Server</c> collection response
/// (<c>IpBillingDistribuidoraRestApiExtensions.PageResponse</c>, XD01-301).
/// </summary>
internal sealed record RestPage<T>(IReadOnlyList<T>? Items, int TotalCount, int Page, int PageSize)
{
    public static PagedResult<T> ToPagedResult(RestPage<T>? page) => new()
    {
        Items = page?.Items ?? [],
        TotalCount = page?.TotalCount ?? 0,
    };
}
