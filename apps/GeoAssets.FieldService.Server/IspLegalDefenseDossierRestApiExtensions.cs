using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using GeoAssets.Core.Models;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Emergency legal defense dossier generator (XD01-286), built on XD01-285's pole audit evidence
/// repository. All compilation logic lives in <see cref="PoleLegalDefenseDossierGenerator"/>
/// (<c>GeoAssets.Core</c>); this is the thin HTTP façade, same division of responsibility as
/// <c>IspComplianceDossierRestApiExtensions</c>.
/// </summary>
internal static class IspLegalDefenseDossierRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspLegalDefenseDossierApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/legal-defense")
    {
        routes.MapPost($"{prefix}/dossier", GenerateAsync)
            .WithName("GenerateEmergencyLegalDefenseDossier")
            .WithSummary("Compiles one or more poles' audit evidence into a certified legal defense dossier, zipped with its attachments.")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GenerateAsync(
        PoleLegalDefenseDossierRequest request, IGeoCoreClient geoCoreClient,
        IPoleAuditEvidenceRepository repository, TimeProvider timeProvider, CancellationToken ct)
    {
        if (request.PoleFeatureIds.Count == 0)
            return Results.BadRequest("PoleFeatureIds must not be empty.");

        var page = await geoCoreClient.QueryFeaturesAsync(
            new AssetQuery { FeatureIds = request.PoleFeatureIds, Take = request.PoleFeatureIds.Count }, ct).ConfigureAwait(false);

        if (page.Items.Count == 0)
            return Results.NotFound();

        var foundIds = page.Items.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var notFoundIds = request.PoleFeatureIds.Where(id => !foundIds.Contains(id)).ToList();
        var bundles = page.Items.Select(f => repository.BuildDefenseBundle(f.Id, request.ValidInstallationTimestampUtc)).ToList();

        var result = PoleLegalDefenseDossierGenerator.Generate(bundles, notFoundIds, timeProvider.GetUtcNow());

        return Results.Bytes(ZipDossier(result), "application/zip", "legal-defense-dossier.zip");
    }

    private static byte[] ZipDossier(PoleLegalDefenseDossierResult result)
    {
        using var memoryStream = new MemoryStream();
        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "legal-defense-dossier.txt", Encoding.UTF8.GetBytes(result.DocumentText));
            WriteEntry(zip, "missing-items.json", JsonSerializer.SerializeToUtf8Bytes(result.MissingItems));

            foreach (var file in result.AttachmentFiles)
                WriteEntry(zip, file.FileName, file.Content);
        }

        return memoryStream.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }
}
