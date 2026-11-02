using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.Server;
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
/// Integration coverage for <c>IspLegalDefenseDossierRestApiExtensions.MapIspLegalDefenseDossierApi</c>
/// (XD01-286): status-code mapping, the zip packaging of document/missing-items/attachments, and
/// the "every endpoint requires an authenticated caller" boundary, mirroring
/// <see cref="IspComplianceDossierRoutingTests"/>'s host bootstrap.
/// </summary>
public sealed class IspLegalDefenseDossierRoutingTests
{
    private const string DossierUri = "/api/field-service/isp/legal-defense/dossier";
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

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

    private static GeoFeature PoleFeature(string id) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-46.633, -23.55),
    };

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
                    app.UseEndpoints(endpoints => endpoints.MapIspLegalDefenseDossierApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static async Task<(string DocumentText, List<PoleLegalDefenseMissingItem> MissingItems, List<ZipArchiveEntry> AttachmentEntries)> ReadDossierZipAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        using var documentStream = await archive.GetEntry("legal-defense-dossier.txt")!.OpenAsync();
        using var documentReader = new StreamReader(documentStream);
        var documentText = await documentReader.ReadToEndAsync();

        using var missingItemsStream = await archive.GetEntry("missing-items.json")!.OpenAsync();
        var missingItems = (await JsonSerializer.DeserializeAsync<List<PoleLegalDefenseMissingItem>>(missingItemsStream))!;

        var attachmentEntries = archive.Entries
            .Where(e => e.FullName is not "legal-defense-dossier.txt" and not "missing-items.json")
            .ToList();

        return (documentText, missingItems, attachmentEntries);
    }

    [Fact]
    public async Task GenerateDossier_EmptyPoleFeatureIds_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new PoleLegalDefenseDossierRequest([], FixedUtcNow));

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
            new PoleLegalDefenseDossierRequest(["pole-missing"], FixedUtcNow));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenerateDossier_WellFormedRequestWithSeededEvidence_Returns200WithExpectedZipContents()
    {
        var validInstallationTimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var fixedUtcNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var anchorEvidence = new PoleAuditEvidenceEntry(
            "pole-1", PoleAuditEvidenceKind.AnchorPhoto, validInstallationTimestampUtc.AddDays(1), -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);
        repository.AddEvidence(anchorEvidence);
        var license = new PoleLicenseRecord("pole-1", "SCM-123", "Acme Telecom", validInstallationTimestampUtc.AddYears(-1), fixedUtcNow.AddYears(1));
        repository.AddLicenseRecord(license);

        var queryResult = new PagedResult<GeoFeature> { Items = [PoleFeature("pole-1")], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult), repository, new FakeTimeProvider(fixedUtcNow));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new PoleLegalDefenseDossierRequest(["pole-1"], validInstallationTimestampUtc));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("legal-defense-dossier.zip");

        var (documentText, missingItems, attachmentEntries) = await ReadDossierZipAsync(response);
        documentText.Should().Contain("EMERGENCY LEGAL DEFENSE DOSSIER");
        documentText.Should().Contain("--- Pole pole-1 ---");
        missingItems.Should().Contain(m =>
            m.PoleFeatureId == "pole-1" && m.Description.Contains("YellowTagPhoto", StringComparison.Ordinal));
        missingItems.Should().Contain(m =>
            m.PoleFeatureId == "pole-1" && m.Description.Contains("BandPosition500mmPhoto", StringComparison.Ordinal));
        attachmentEntries.Select(e => e.FullName).Should().Contain(
            ["pole-1/evidence-AnchorPhoto-0.jpg", "pole-1/license-0.txt"]);
    }

    [Fact]
    public async Task GenerateDossier_MixOfResolvableAndUnresolvableIds_Returns200WithBothKindsOfMissingItems()
    {
        var validInstallationTimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var evidence = new PoleAuditEvidenceEntry(
            "pole-1", PoleAuditEvidenceKind.AnchorPhoto, validInstallationTimestampUtc.AddDays(1), -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);
        repository.AddEvidence(evidence);

        var queryResult = new PagedResult<GeoFeature> { Items = [PoleFeature("pole-1")], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult), repository);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new PoleLegalDefenseDossierRequest(["pole-1", "pole-unresolvable"], validInstallationTimestampUtc));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, missingItems, attachmentEntries) = await ReadDossierZipAsync(response);

        missingItems.Should().Contain(m =>
            m.PoleFeatureId == "pole-unresolvable" && m.Description == "Pole feature id does not resolve to a known feature.");
        missingItems.Should().Contain(m =>
            m.PoleFeatureId == "pole-1" && m.Description.Contains("No SCM license record", StringComparison.Ordinal));
        attachmentEntries.Select(e => e.FullName).Should().Contain("pole-1/evidence-AnchorPhoto-0.jpg");
    }

    [Fact]
    public async Task GenerateDossier_NoAuthenticatedPrincipal_Returns401()
    {
        var queryResult = new PagedResult<GeoFeature> { Items = [PoleFeature("pole-1")], TotalCount = 1 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri(DossierUri, UriKind.Relative),
            new PoleLegalDefenseDossierRequest(["pole-1"], FixedUtcNow));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
