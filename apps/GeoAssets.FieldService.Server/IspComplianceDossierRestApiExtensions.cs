using System.IO.Compression;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Models;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Concessionaire compliance dossier exporter (XD01-284), built on XD01-279's ISP vertical host
/// scaffolding. The thin HTTP façade over <see cref="DossierExporterRegistry"/> and
/// whichever <see cref="IDossierExporter"/> it resolves — all the real format logic
/// lives in <c>GeoAssets.Core.Isp.Compliance</c>.
/// </summary>
internal static class IspComplianceDossierRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspComplianceDossierApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/compliance")
    {
        routes.MapPost($"{prefix}/dossier", GenerateAsync)
            .WithName("GenerateConcessionaireComplianceDossier")
            .WithSummary("Generates a concessionaire pole-sharing compliance dossier (e.g. DIS-NOR-056 or JoiN-AS) for the given pole features, zipped into one archive.")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GenerateAsync(
        IspComplianceDossierRequest request,
        IGeoCoreClient geoCoreClient,
        DossierExporterRegistry registry,
        CancellationToken ct)
    {
        if (request.PoleFeatureIds.Count == 0)
            return Results.BadRequest("PoleFeatureIds must not be empty.");

        if (!registry.TryResolve(request.DistributorName, out var exporter))
        {
            return Results.BadRequest(
                $"Unknown distributor '{request.DistributorName}'. Registered: {string.Join(", ", registry.RegisteredDistributors)}.");
        }

        var page = await geoCoreClient.QueryFeaturesAsync(
            new AssetQuery { FeatureIds = request.PoleFeatureIds, Take = request.PoleFeatureIds.Count },
            ct).ConfigureAwait(false);

        if (page.Items.Count == 0)
            return Results.NotFound();

        try
        {
            var result = exporter.Export(page.Items);
            return Results.Bytes(ZipDossierFiles(result.Files), "application/zip", $"{request.DistributorName}-dossier.zip");
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static byte[] ZipDossierFiles(IReadOnlyList<DossierFile> files)
    {
        using var memoryStream = new MemoryStream();
        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.FileName);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content);
            }
        }

        return memoryStream.ToArray();
    }
}
