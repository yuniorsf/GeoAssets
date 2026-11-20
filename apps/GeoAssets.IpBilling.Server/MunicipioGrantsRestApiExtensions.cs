namespace GeoAssets.IpBilling.Server;

/// <summary>
/// The município↔Prefeitura grant-provisioning endpoints (XD01-177) — moved from the kernel's
/// <c>/api/identity/municipio-grants/*</c> (the IP-billing bridge module) to this host under its own prefix by XD01-385,
/// reached through the gateway's <c>/api/ip-billing/**</c> route:
/// <list type="bullet">
///   <item><c>POST {prefix}/municipio-grants/sync</c> — <see cref="MunicipioProvisioningService.SyncGrantAsync"/>;</item>
///   <item><c>POST {prefix}/municipio-grants/revoke</c> — <see cref="MunicipioProvisioningService.RevokeAsync"/>.</item>
/// </list>
/// Authorized as before: the <c>ip-billing:reference-manage</c> permission, evaluated by the kernel for the caller's own
/// token (<c>AddKernelIdentity</c> + <c>AddGeoPermissionPolicies</c>), and a <c>GeoAssets.ServiceCaller</c> service
/// principal passes it. The organizations are the body's, not the caller's: a reference-data manager provisions grants
/// for any Distribuidora/Prefeitura pair, as the kernel's endpoints allowed. Both answer 204; an invalid body (or one the kernel refuses) answers
/// 400, an unavailable kernel 503, and a kernel that rejects the caller's token 502
/// (<see cref="GeoAssets.Workflow.Hosting.KernelFailureTranslation"/>, XD01-390).
/// </summary>
internal static class MunicipioGrantsRestApiExtensions
{
    /// <summary>The permission both endpoints require.</summary>
    public const string ManagePermission = "ip-billing:reference-manage";

    public static IEndpointRouteBuilder MapMunicipioGrantsApi(this IEndpointRouteBuilder routes, string prefix = "/api/ip-billing")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost($"{prefix}/municipio-grants/sync", async (
            MunicipioGrantSyncRequest req,
            MunicipioProvisioningService provisioning,
            CancellationToken ct) =>
        {
            try
            {
                await provisioning.SyncGrantAsync(
                    req.IbgeCode, req.DistribuidoraOrganizationId, req.PrefeituraOrganizationId,
                    req.PreviousPrefeituraOrganizationId, req.GrantedBy, ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        routes.MapPost($"{prefix}/municipio-grants/revoke", async (
            MunicipioGrantRevokeRequest req,
            MunicipioProvisioningService provisioning,
            CancellationToken ct) =>
        {
            try
            {
                await provisioning.RevokeAsync(
                    req.IbgeCode, req.DistribuidoraOrganizationId, req.PrefeituraOrganizationId, ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        return routes;
    }
}
