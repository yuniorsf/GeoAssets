using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services.Evidence;
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
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>IspPoleAuditRestApiExtensions.MapIspPoleAuditApi</c> (XD01-285):
/// per-endpoint status-code mapping, the evidence-verification/descriptor-extraction path, the
/// real dossier-exporter wiring (mirroring <see cref="IspComplianceDossierRoutingTests"/>), and
/// the "every endpoint requires an authenticated caller" boundary.
/// </summary>
public sealed class IspPoleAuditRoutingTests
{
    private const string Prefix = "/api/field-service/isp/pole-audit";
    private const string TenantSecret = "tenant-secret";
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGeoCoreClient(GeoFeature? feature = null) : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(feature);

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static GeoFeature PoleFeature(string id = "pole-1") => new()
    {
        Id = id,
        Geometry = new GeoPoint(-46.633, -23.55),
    };

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

    private static byte[] BuildSignedJpeg(PoleAuditEvidenceKind kind, DateTimeOffset capturedAtUtc, string tenantSecret, double latitude = -23.55052, double longitude = -46.633309)
    {
        byte[] rawJpeg = [0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9];
        var signature = EvidenceHmacSigner.ComputeSignature(rawJpeg, latitude, longitude, capturedAtUtc, "order-1", tenantSecret);
        var descriptor = new EvidenceSignatureDescriptor(signature, latitude, longitude, capturedAtUtc, "order-1");
        return JpegExifImageDescriptionCodec.WriteImageDescription(rawJpeg, descriptor.ToDescriptorString());
    }

    private static async Task<TestServer> BuildServerAsync(
        IGeoCoreClient? geoCoreClient = null, IPoleAuditEvidenceRepository? repository = null, FakeTimeProvider? timeProvider = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(geoCoreClient ?? new FakeGeoCoreClient());
                    services.AddSingleton(repository ?? new InMemoryPoleAuditEvidenceRepository());
                    services.AddSingleton<TimeProvider>(timeProvider ?? new FakeTimeProvider());
                    services.AddSingleton<IShapefileExportService, ShapefileExportService>();
                    services.AddSingleton<IDossierExporter, DisNor056Exporter>();
                    services.AddSingleton<IDossierExporter, JoinAsExporter>();
                    services.AddSingleton<DossierExporterRegistry>();
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
                    app.UseEndpoints(endpoints => endpoints.MapIspPoleAuditApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    // Evidence endpoint

    [Fact]
    public async Task AddEvidence_PoleMissing_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var signed = BuildSignedJpeg(PoleAuditEvidenceKind.AnchorPhoto, FixedUtcNow, TenantSecret);

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-missing/evidence", UriKind.Relative),
            new PoleAuditEvidenceIngestRequest(signed, TenantSecret, PoleAuditEvidenceKind.AnchorPhoto));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddEvidence_UntamperedSignedJpegWithCorrectTenantSecret_Returns201AndStoresEntryWithDescriptorFields()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")), repository);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var capturedAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var signed = BuildSignedJpeg(PoleAuditEvidenceKind.YellowTagPhoto, capturedAt, TenantSecret, latitude: -23.1, longitude: -46.2);

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/evidence", UriKind.Relative),
            new PoleAuditEvidenceIngestRequest(signed, TenantSecret, PoleAuditEvidenceKind.YellowTagPhoto));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var bundle = repository.BuildDefenseBundle("pole-1", DateTimeOffset.MinValue);
        var entry = bundle.Evidence.Should().ContainSingle().Which;
        entry.PoleFeatureId.Should().Be("pole-1");
        entry.Kind.Should().Be(PoleAuditEvidenceKind.YellowTagPhoto);
        entry.CapturedAtUtc.Should().Be(capturedAt);
        entry.Latitude.Should().Be(-23.1);
        entry.Longitude.Should().Be(-46.2);
    }

    [Fact]
    public async Task AddEvidence_TamperedJpeg_Returns400()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var signed = BuildSignedJpeg(PoleAuditEvidenceKind.AnchorPhoto, FixedUtcNow, TenantSecret);
        signed[^1] ^= 0xFF;

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/evidence", UriKind.Relative),
            new PoleAuditEvidenceIngestRequest(signed, TenantSecret, PoleAuditEvidenceKind.AnchorPhoto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddEvidence_WrongTenantSecret_Returns400()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var signed = BuildSignedJpeg(PoleAuditEvidenceKind.AnchorPhoto, FixedUtcNow, TenantSecret);

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/evidence", UriKind.Relative),
            new PoleAuditEvidenceIngestRequest(signed, "wrong-secret", PoleAuditEvidenceKind.AnchorPhoto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddEvidence_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();
        var signed = BuildSignedJpeg(PoleAuditEvidenceKind.AnchorPhoto, FixedUtcNow, TenantSecret);

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/evidence", UriKind.Relative),
            new PoleAuditEvidenceIngestRequest(signed, TenantSecret, PoleAuditEvidenceKind.AnchorPhoto));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // License endpoint

    [Fact]
    public async Task AddLicenseRecord_PoleMissing_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-missing/license", UriKind.Relative),
            new PoleLicenseRecordRequest("SCM-123", "Acme Telecom", FixedUtcNow, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddLicenseRecord_WellFormedRequest_Returns201()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/license", UriKind.Relative),
            new PoleLicenseRecordRequest("SCM-123", "Acme Telecom", FixedUtcNow, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("", "Acme Telecom")]
    [InlineData("   ", "Acme Telecom")]
    [InlineData("SCM-123", "")]
    [InlineData("SCM-123", "   ")]
    public async Task AddLicenseRecord_EmptyOrWhitespaceField_Returns400(string scmLicenseNumber, string licenseHolderName)
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/license", UriKind.Relative),
            new PoleLicenseRecordRequest(scmLicenseNumber, licenseHolderName, FixedUtcNow, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddLicenseRecord_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/license", UriKind.Relative),
            new PoleLicenseRecordRequest("SCM-123", "Acme Telecom", FixedUtcNow, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Dossier endpoint

    [Fact]
    public async Task GenerateDossier_PoleMissing_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-missing/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Enel"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenerateDossier_WellFormedEnelRequestAgainstQualifyingPole_Returns201WithExpectedFileNames()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Enel"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileNames = await response.Content.ReadFromJsonAsync<List<string>>();
        fileNames.Should().BeEquivalentTo(["join-as-manifest.csv", "join-as-manifest.xml"]);
    }

    [Fact]
    public async Task GenerateDossier_WellFormedNeoenergiaRequestAgainstQualifyingPole_Returns201WithExpectedFileNames()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Neoenergia"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileNames = await response.Content.ReadFromJsonAsync<List<string>>();
        fileNames.Should().BeEquivalentTo(["dis-nor-056-poles.dxf", "dis-nor-056-poles-shapefile.zip"]);
    }

    [Fact]
    public async Task GenerateDossier_UnknownDistributor_Returns400()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Unknown Distributor"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateDossier_PoleDoesNotQualifyForChosenExporter_Returns400()
    {
        var nonQualifying = new GeoFeature
        {
            Id = "pole-1",
            Geometry = new GeoPoint(-46.633, -23.55),
            Properties = new GeoFeatureProperties { AssetTypeId = Guid.NewGuid().ToString() },
        };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(nonQualifying));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Enel"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateDossier_NoAuthenticatedPrincipal_Returns401()
    {
        var feature = PonnotPoleFeature("pole-1", "COD-1");
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"{Prefix}/pole-1/dossier", UriKind.Relative),
            new PoleDossierRecordRequest("Enel"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Defense-bundle endpoint

    [Fact]
    public async Task GetDefenseBundle_PoleMissing_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(
            new Uri($"{Prefix}/pole-missing/defense-bundle?validInstallationTimestampUtc=2026-01-01T00:00:00Z", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDefenseBundle_SeededRecords_Returns200FilteredByCutoff()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var cutoff = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var staleEvidence = new PoleAuditEvidenceEntry(
            "pole-1", PoleAuditEvidenceKind.AnchorPhoto, cutoff.AddDays(-1), -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);
        var freshEvidence = new PoleAuditEvidenceEntry(
            "pole-1", PoleAuditEvidenceKind.YellowTagPhoto, cutoff.AddDays(1), -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);
        repository.AddEvidence(staleEvidence);
        repository.AddEvidence(freshEvidence);
        repository.AddLicenseRecord(new PoleLicenseRecord("pole-1", "SCM-123", "Acme Telecom", cutoff.AddYears(-1), null));
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")), repository);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(new Uri(
            $"{Prefix}/pole-1/defense-bundle?validInstallationTimestampUtc={Uri.EscapeDataString(cutoff.ToString("O"))}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bundle = await response.Content.ReadFromJsonAsync<PoleDefenseBundle>();
        bundle!.Evidence.Should().ContainSingle().Which.Kind.Should().Be(PoleAuditEvidenceKind.YellowTagPhoto);
        bundle.LicenseRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task GetDefenseBundle_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(PoleFeature("pole-1")));
        using var client = server.CreateClient();

        var response = await client.GetAsync(
            new Uri($"{Prefix}/pole-1/defense-bundle?validInstallationTimestampUtc=2026-01-01T00:00:00Z", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
