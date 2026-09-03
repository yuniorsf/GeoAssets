using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IOrganizationRepository"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/organizations</c> endpoints (XD01-128). See <see cref="RestRoleRepository"/>'s
/// doc comment for the DTO-mapping and no-op <see cref="SaveChangesAsync"/> rationale — the same
/// applies here.
///
/// <see cref="GetBySlugAsync"/> has no matching server endpoint (only by-id lookup exists) and
/// throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class RestOrganizationRepository(HttpClient http) : IOrganizationRepository
{
    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"organizations/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<OrganizationDto>(ct)
            ?? throw new InvalidOperationException($"GET organizations/{id} returned an empty response.");
        return ToOrganization(dto);
    }

    public async Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<OrganizationDto>>("organizations", ct) ?? [];
        return dtos.Select(ToOrganization).ToList();
    }

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<UserSummaryDto>>($"organizations/{organizationId}/users", ct) ?? [];
        return dtos.Select(ToUser).ToList();
    }

    /// <summary>
    /// The server always mints its own <c>Id</c> for a new organization (see
    /// <c>IdentityRestApiExtensions</c>'s <c>POST /organizations</c> — <see cref="OrganizationWriteDto"/>
    /// carries no <c>Id</c> field). Same Location-header pattern as <see cref="RestRoleRepository.AddAsync"/>.
    /// </summary>
    public async Task AddAsync(Organization organization, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("organizations",
            new OrganizationWriteDto(organization.Name, organization.Slug, organization.Description, organization.IsActive), ct);
        response.EnsureSuccessStatusCode();

        var location = response.Headers.Location
            ?? throw new InvalidOperationException("POST organizations did not return a Location header.");
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        organization.Id = Guid.Parse(path.TrimEnd('/').Split('/')[^1]);
    }

    public async Task UpdateAsync(Organization organization, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"organizations/{organization.Id}",
            new OrganizationWriteDto(organization.Name, organization.Slug, organization.Description, organization.IsActive), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static Organization ToOrganization(OrganizationDto dto) => new()
    {
        Id          = dto.Id,
        Name        = dto.Name,
        Slug        = dto.Slug,
        Description = dto.Description,
        IsActive    = dto.IsActive,
        CreatedAt   = dto.CreatedAt,
    };

    // UserSummaryDto has no CreatedAt — CreatedAt = default mirrors RestUserRepository's own
    // ToUser(UserSummaryDto) mapping.
    private static AppUser ToUser(UserSummaryDto dto) => new()
    {
        Id             = dto.Id,
        Email          = dto.Email,
        DisplayName    = dto.DisplayName,
        IsActive       = dto.IsActive,
        OrganizationId = dto.OrganizationId,
        CreatedAt      = default,
    };

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
}
