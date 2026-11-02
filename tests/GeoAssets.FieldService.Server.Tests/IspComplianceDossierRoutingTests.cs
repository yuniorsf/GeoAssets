using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.Server;
using GeoAssets.Provider.Shapefile;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>IspComplianceDossierRestApiExtensions.MapIspComplianceDossierApi</c>
/// (XD01-284): distributor resolution, status-code mapping, and the "every endpoint requires an
/// authenticated caller" boundary, mirroring <see cref="IspCtoOccupancyRoutingTests"/>'s host
/// bootstrap. Uses the REAL <see cref="DisNor056Exporter"/>/<see cref="JoinAsExporter"/>/
/// <see cref="ConcessionaireDossierExporterRegistry"/> and the REAL
/// <see cref="ShapefileExportService"/> (not faked) to prove the end-to-end DI wiring that
/// <c>Program.cs</c> performs actually produces a working dossier.
/// </summary>
public sealed class IspComplianceDossierRoutingTests
{
    private const string DossierUri = "/api/field-service/isp/compliance/dossier";

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGeoCoreClient(PagedResult<GeoFeature>? queryResult = null) : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            Task.FromResult(queryResult ?? new PagedResult<GeoFeature> { Items = [], TotalCount = 0 });

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static GeoFeature PonnotPoleFeature(string id, string codId, double lon = -46.633, double lat = -23.55) => new()
    {
        Id = id,
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = BdgdAssetTypeIds.Ponnot.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["COD_ID"] = codId },
        },
    };

    private static async Task<TestServer> BuildServerAsync(IGeoCoreClient? geoCoreClient = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(geoCoreClient ?? new FakeGeoCoreClient());
                    services.AddSingleton<IShapefileExportService, ShapefileExportService>();
                    services.AddSingleton<IConcessionaireDossierExporter, DisNor056Exporter>();
                    services.AddSingleton<IConcessionaireDossierExporter, JoinAsExporter>();
                    services.AddSingleton<ConcessionaireDossierExporterRegistry>();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapIspComplianceDossierApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task GenerateDossier_WellFormedEnelRequest_Returns200WithCsvAndXmlEntries()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        var queryResult = new PagedResult<GeoFeature> { Items = [feature], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Enel", ["pole-1"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(["join-as-manifest.csv", "join-as-manifest.xml"]);
    }

    [Fact]
    public async Task GenerateDossier_WellFormedNeoenergiaRequest_Returns200WithDxfAndShapefileEntries()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        var queryResult = new PagedResult<GeoFeature> { Items = [feature], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Neoenergia", ["pole-1"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(["dis-nor-056-poles.dxf", "dis-nor-056-poles-shapefile.zip"]);
    }

    [Fact]
    public async Task GenerateDossier_UnknownDistributor_Returns400()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        var queryResult = new PagedResult<GeoFeature> { Items = [feature], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Unknown Distributor", ["pole-1"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateDossier_EmptyPoleFeatureIds_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Enel", []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateDossier_QueryReturnsZeroFeatures_Returns404()
    {
        var queryResult = new PagedResult<GeoFeature> { Items = [], TotalCount = 0 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Enel", ["pole-missing"]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenerateDossier_FeaturesReturnedButNoneQualify_Returns400()
    {
        var nonQualifying = new GeoFeature
        {
            Id = "pole-1",
            Geometry = new GeoPoint(-46.633, -23.55),
            Properties = new GeoFeatureProperties { AssetTypeId = Guid.NewGuid().ToString() },
        };
        var queryResult = new PagedResult<GeoFeature> { Items = [nonQualifying], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Enel", ["pole-1"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateDossier_NoAuthenticatedPrincipal_Returns401()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        var queryResult = new PagedResult<GeoFeature> { Items = [feature], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new IspComplianceDossierRequest("Enel", ["pole-1"]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
