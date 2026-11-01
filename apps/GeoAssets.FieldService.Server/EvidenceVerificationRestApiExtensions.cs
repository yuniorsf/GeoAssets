using GeoAssets.Core.Services.Evidence;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the evidence-verification endpoint (XD01-278) onto any <see cref="IEndpointRouteBuilder"/>
/// — the concrete "verifiable server-side on ingestion" surface the AC asks for.
/// <see cref="EvidenceVerificationService.Verify"/> (<c>GeoAssets.Core</c>) already implements
/// the full re-derive-and-compare logic (shared with <c>EvidenceCaptureService.Verify</c> in the
/// MAUI/Shared capture flow, XD01-244); this file is only the thin HTTP façade, same division of
/// responsibility <see cref="FieldWorkInspectionRestApiExtensions"/> documents for its own
/// endpoint over <c>InspectionCommitService</c>.
/// </summary>
internal static class EvidenceVerificationRestApiExtensions
{
    public static IEndpointRouteBuilder MapEvidenceVerificationApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/evidence")
    {
        routes.MapPost($"{prefix}/verify", (VerifyEvidenceRequest request) =>
        {
            var result = EvidenceVerificationService.Verify(request.SignedJpegBytes, request.TenantSecret);
            return Results.Ok(result);
        })
        .WithName("VerifyEvidence")
        .WithSummary("Re-verifies a captured evidence photo's HMAC-SHA256 signature server-side; fails if the image or its signed metadata was altered after capture.")
        .Produces<EvidenceVerificationResult>();

        return routes;
    }
}
