using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using GeoAssets.Core.Services.Evidence;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Pole-sharing audit & yellow-tag legal defense evidence repository (XD01-285), built on
/// XD01-278's evidence sealing and XD01-284's compliance dossier exporter. All domain logic lives
/// in <see cref="IPoleAuditEvidenceRepository"/> (<c>GeoAssets.Core</c>); this is the thin HTTP
/// façade, same division of responsibility as <c>IspComplianceDossierRestApiExtensions</c>.
/// </summary>
internal static class IspPoleAuditRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspPoleAuditApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/pole-audit")
    {
        routes.MapPost($"{prefix}/{{poleId}}/evidence", AddEvidenceAsync)
            .WithName("AddPoleAuditEvidence")
            .WithSummary("Verifies an HMAC-sealed field photo server-side and links it to the audited pole.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapPost($"{prefix}/{{poleId}}/license", AddLicenseRecordAsync)
            .WithName("AddPoleLicenseRecord")
            .WithSummary("Links the ISP's SCM license data to the audited pole.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapPost($"{prefix}/{{poleId}}/dossier", GenerateDossierAsync)
            .WithName("GeneratePoleComplianceDossierRecord")
            .WithSummary("Generates a concessionaire compliance dossier for the pole and links it to the audit record.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapGet($"{prefix}/{{poleId}}/defense-bundle", GetDefenseBundleAsync)
            .WithName("GetPoleDefenseBundle")
            .WithSummary("Returns everything needed for an injunction defense for the given pole in one call.")
            .Produces<PoleDefenseBundle>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> AddEvidenceAsync(
        string poleId, PoleAuditEvidenceIngestRequest request, IGeoCoreClient geoCoreClient,
        IPoleAuditEvidenceRepository repository, CancellationToken ct)
    {
        if (await geoCoreClient.GetFeatureByIdAsync(poleId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        var verification = EvidenceVerificationService.Verify(request.SignedJpegBytes, request.TenantSecret);
        if (!verification.IsValid)
            return Results.BadRequest(verification.FailureReason);

        if (!JpegExifImageDescriptionCodec.TryReadImageDescription(request.SignedJpegBytes, out var raw)
            || !EvidenceSignatureDescriptor.TryParse(raw, out var descriptor) || descriptor is null)
        {
            return Results.BadRequest("Evidence signature descriptor is malformed.");
        }

        repository.AddEvidence(new PoleAuditEvidenceEntry(
            poleId, request.Kind, descriptor.CapturedAtUtc, descriptor.Latitude, descriptor.Longitude, request.SignedJpegBytes));

        return Results.Created(new Uri($"{poleId}/evidence", UriKind.Relative), (object?)null);
    }

    private static async Task<IResult> AddLicenseRecordAsync(
        string poleId, PoleLicenseRecordRequest request, IGeoCoreClient geoCoreClient,
        IPoleAuditEvidenceRepository repository, CancellationToken ct)
    {
        if (await geoCoreClient.GetFeatureByIdAsync(poleId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.ScmLicenseNumber) || string.IsNullOrWhiteSpace(request.LicenseHolderName))
            return Results.BadRequest("ScmLicenseNumber and LicenseHolderName must not be empty.");

        repository.AddLicenseRecord(new PoleLicenseRecord(
            poleId, request.ScmLicenseNumber, request.LicenseHolderName, request.IssuedAtUtc, request.ExpiresAtUtc));

        return Results.Created(new Uri($"{poleId}/license", UriKind.Relative), (object?)null);
    }

    private static async Task<IResult> GenerateDossierAsync(
        string poleId, PoleDossierRecordRequest request, IGeoCoreClient geoCoreClient,
        ConcessionaireDossierExporterRegistry registry, IPoleAuditEvidenceRepository repository,
        TimeProvider timeProvider, CancellationToken ct)
    {
        var feature = await geoCoreClient.GetFeatureByIdAsync(poleId, ct).ConfigureAwait(false);
        if (feature is null)
            return Results.NotFound();

        if (!registry.TryResolve(request.DistributorName, out var exporter))
        {
            return Results.BadRequest(
                $"Unknown distributor '{request.DistributorName}'. Registered: {string.Join(", ", registry.RegisteredDistributors)}.");
        }

        try
        {
            var result = exporter.Export([feature]);
            repository.AddDossierRecord(new PoleDossierRecord(poleId, request.DistributorName, timeProvider.GetUtcNow(), result));
            return Results.Created(new Uri($"{poleId}/dossier", UriKind.Relative), result.Files.Select(f => f.FileName));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> GetDefenseBundleAsync(
        string poleId, DateTimeOffset validInstallationTimestampUtc, IGeoCoreClient geoCoreClient,
        IPoleAuditEvidenceRepository repository, CancellationToken ct)
    {
        if (await geoCoreClient.GetFeatureByIdAsync(poleId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        return Results.Ok(repository.BuildDefenseBundle(poleId, validInstallationTimestampUtc));
    }
}
