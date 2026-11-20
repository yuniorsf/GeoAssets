using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// The caller's own organization's active grants (XD01-382) as the kernel answers <c>GET /api/identity/organization-grants/me</c>
/// for the caller's own token — the route the Blazor client reads for the same purpose (XD01-332), so the service-order
/// organization gate decides here as it does in the kernel. A kernel that does not answer yields no grant: a grantee is
/// then offered less than the kernel would allow, never more.
/// </summary>
internal sealed class KernelOwnOrganizationGrantSource(HttpClient http) : IOwnOrganizationGrantSource
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri("organization-grants/me", UriKind.Relative), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];

        var grants = await response.Content.ReadFromJsonAsync<List<OwnOrganizationGrantDto>>(_opts, ct).ConfigureAwait(false) ?? [];
        return [.. grants.Select(g => g.ToModel(granteeOrganizationId))];
    }
}
