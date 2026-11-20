namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// One page of <c>GET {prefix}/organization-grants</c> (XD01-385): the active, unexpired grants of one grantee/resource
/// organization pair, oldest first, with the paging the caller asked for (1-based <see cref="Page"/>) and the pair's total.
/// </summary>
public sealed record OrganizationGrantPage(IReadOnlyList<OrganizationGrantDto> Items, int Page, int PageSize, int TotalCount);
