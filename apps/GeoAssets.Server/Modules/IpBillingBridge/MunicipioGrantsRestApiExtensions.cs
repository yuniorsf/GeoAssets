using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server.Modules.IpBillingBridge;

/// <summary>
/// The município↔Prefeitura grant-provisioning endpoints (XD01-177), split out of <c>MapIdentityApi</c> into the IP-billing
/// bridge module (XD01-359) at the same URLs, under the identity prefix, with the same permission.
/// </summary>
public static class MunicipioGrantsRestApiExtensions
{
    /// <summary>
    /// Sync/revoke endpoints for <see cref="MunicipioProvisioningService"/>, called by the
    /// standalone <c>GeoAssets.IpBilling.Server</c> microservice over HTTP whenever a município's
    /// Prefeitura-organization link changes — never a direct assembly reference, per
    /// <c>ServerArchitectureTests</c>/<c>MessagingArchitectureTests</c>' decoupling invariants
    /// (see <see cref="MunicipioProvisioningService"/>'s own doc comment).
    ///
    /// <strong>Known gap, flagged for a follow-up decision:</strong> this codebase has no
    /// service-to-service authentication mechanism yet for a vertical microservice calling back
    /// into <c>GeoAssets.Server</c> — every existing cross-host call (<c>RestGeoCoreClient</c>/
    /// <c>HttpPoleResolver</c>) is a read-only, unauthenticated lookup, which is a materially
    /// different risk than this grant-provisioning write. These two endpoints are therefore gated
    /// behind the real <c>ip-billing:reference-manage</c> permission check like every other
    /// sensitive write in this file, but <c>GeoAssets.IpBilling.Server</c> has no caller identity
    /// to present yet — it isn't itself an authenticated principal. Until a real service-identity
    /// story exists (e.g. a client-credentials token, mTLS, or HMAC-signed internal requests),
    /// <c>GeoAssets.IpBilling.Server</c>'s own município write endpoints cannot actually reach
    /// these two without a human first deciding how that credential works — they are NOT wired up
    /// from that side in this ticket. See the ticket's own closing comment for the filed follow-up.
    /// </summary>
    public static IEndpointRouteBuilder MapMunicipioGrantsApi(this IEndpointRouteBuilder routes, string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost($"{prefix}/municipio-grants/sync", async (
            MunicipioGrantSyncRequest req,
            MunicipioProvisioningService provisioning,
            CancellationToken ct) =>
        {
            await provisioning.SyncGrantAsync(
                req.IbgeCode, req.DistribuidoraOrganizationId, req.PrefeituraOrganizationId,
                req.PreviousPrefeituraOrganizationId, req.GrantedBy, ct).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("ip-billing:reference-manage");

        routes.MapPost($"{prefix}/municipio-grants/revoke", async (
            MunicipioGrantRevokeRequest req,
            MunicipioProvisioningService provisioning,
            CancellationToken ct) =>
        {
            await provisioning.RevokeAsync(
                req.IbgeCode, req.DistribuidoraOrganizationId, req.PrefeituraOrganizationId, ct).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("ip-billing:reference-manage");

        return routes;
    }
}
