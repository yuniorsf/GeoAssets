using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.Server.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The tile endpoints over PostGIS (XD01-434), on a database built by the production migrations and with the
/// <c>postgres-spatial</c> module's wiring — organization catalogs and their warm-up middleware,
/// <c>AddPostgresVectorTileSource</c>, <c>AddPostgresAssetRenderQuery</c> + <c>AddSkiaRasterTileSource</c>,
/// <c>AddTileServing</c> (whose <see cref="ITileInvalidation"/> the PostGIS writers report to) and <c>MapTileApi</c>, in the
/// <c>Production</c> environment: an organization's caller gets its organization's and the shared features in its tiles,
/// never another organization's — from the source and from the cache; a committed write through the organization catalog
/// evicts the tile and changes <c>{v}</c>; a mutation batch that rolls back evicts nothing; and no tile request loads the
/// caller's organization catalog (<c>WarmCacheAsync</c>).
/// </summary>
public sealed class TileEndpointTenantScopingTests
{
    private const string OrganizationClaim = "test_org";
    private const string OrganizationHeader = "X-Test-Organization";
    private const string NoOrganization = "none";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";
    private const int Zoom = 16;

    private static readonly Guid OrganizationA = new("a1a1a1a1-0000-4000-8000-000000000434");
    private static readonly Guid OrganizationB = new("b2b2b2b2-0000-4000-8000-000000000434");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // One z16 tile (~600 m) holding the three poles, 50 m apart.
    private static readonly TileAddress Tile = TileAddress.Containing(-38.5, -13.0, Zoom);
    private static readonly (double Lon, double Lat) Center = (
        (Tile.Envelope4326.MinLon + Tile.Envelope4326.MaxLon) / 2, (Tile.Envelope4326.MinLat + Tile.Envelope4326.MaxLat) / 2);

    private static readonly Pole PoleOfA = new("tile-pole-org-a", OrganizationA, Center.Lon - 0.0005, Center.Lat);
    private static readonly Pole PoleOfB = new("tile-pole-org-b", OrganizationB, Center.Lon, Center.Lat);
    private static readonly Pole SharedPole = new("tile-pole-shared", Guid.Empty, Center.Lon + 0.0005, Center.Lat);

    [Fact]
    public async Task VectorTiles_ShowEachCallerItsOrganizationAndSharedFeatures_FromTheSourceAndFromTheCache()
    {
        await using var host = await HostAsync();

        foreach (var pass in new[] { "drawn", "cached" })
        {
            (await IdsInTileAsync(host, NoOrganization)).Should().BeEquivalentTo([SharedPole.Id], because: "{0}: organization-less", pass);
            (await IdsInTileAsync(host, OrganizationA.ToString())).Should().BeEquivalentTo([PoleOfA.Id, SharedPole.Id], because: "{0}: organization A", pass);
            (await IdsInTileAsync(host, OrganizationB.ToString())).Should().BeEquivalentTo([PoleOfB.Id, SharedPole.Id], because: "{0}: organization B", pass);
        }

        host.Cache.Count.Should().Be(3, "one entry per scope, never shared");
    }

    [Fact]
    public async Task RasterTiles_AreServedPerOrganization_AsPngs()
    {
        await using var host = await HostAsync(rasterTier: true);
        using var a = host.ClientFor(OrganizationA.ToString());
        using var b = host.ClientFor(OrganizationB.ToString());

        var tile = Tile.Parent!.Value;
        using var ofA = await a.GetAsync(new Uri($"/tiles/assets/{await VersionAsync(a)}/{tile}.png", UriKind.Relative));
        using var ofB = await b.GetAsync(new Uri($"/tiles/assets/{await VersionAsync(b)}/{tile}.png", UriKind.Relative));

        ofA.StatusCode.Should().Be(HttpStatusCode.OK);
        ofA.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await ofA.Content.ReadAsByteArrayAsync()).Should().NotEqual(await ofB.Content.ReadAsByteArrayAsync(), "each draws its own organization's pole");
        host.Cache.Count.Should().Be(8, "each organization's whole 2 × 2 metatile is cached from its one drawing");
    }

    [Fact]
    public async Task ACommittedWrite_EvictsTheTile_AndChangesTheVersion_AndARolledBackBatchEvictsNothing()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrganizationA.ToString());
        var before = await VersionAsync(client);
        (await IdsInTileAsync(host, OrganizationA.ToString())).Should().NotContain("tile-pole-new");

        var catalogs = host.Services.GetRequiredService<IOrganizationCatalogFactory>();
        catalogs.GetForOrganization(OrganizationA).Add(Feature(new Pole("tile-pole-new", OrganizationA, Center.Lon, Center.Lat + 0.0005)));
        var after = await VersionAsync(client);

        after.Should().NotBe(before);
        (await IdsInTileAsync(host, OrganizationA.ToString())).Should().Contain("tile-pole-new", "the write evicted the cached tile");

        var cached = host.Cache.Count;
        var generation = host.Services.GetRequiredService<TileVersions>().Generation;
        var applier = host.Services.GetRequiredService<IAssetMutationBatchApplier>();
        var rollback = AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "default",
            organizationId: OrganizationA,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations: [Attributes(PoleOfA.Id), Attributes("tile-pole-missing")],
            timeProvider: TimeProvider.System,
            batchId: Guid.NewGuid(),
            sourceOrderId: "order-1",
            correlationId: "order-1");
        var act = () => applier.ApplyAsync(rollback, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        host.Services.GetRequiredService<TileVersions>().Generation.Should().Be(generation, "a rolled-back batch is never invalidated");
        host.Cache.Count.Should().Be(cached);
        (await VersionAsync(client)).Should().Be(after);
    }

    [Fact]
    public async Task TileRequests_NeverLoadTheCallersOrganizationCatalog()
    {
        await using var host = await HostAsync(rasterTier: true);
        host.Logs.Clear();

        using var client = host.ClientFor(OrganizationA.ToString());
        var version = await VersionAsync(client);
        using var raster = await client.GetAsync(new Uri($"/tiles/assets/{version}/{Tile.Parent!.Value}.png", UriKind.Relative));
        using var vector = await client.GetAsync(new Uri($"/tiles/assets/{version}/{Tile}.mvt", UriKind.Relative));

        raster.StatusCode.Should().Be(HttpStatusCode.OK);
        vector.StatusCode.Should().Be(HttpStatusCode.OK);

        host.Logs.Should().NotContain(m => m.Contains("WarmCacheAsync", StringComparison.Ordinal) || m.Contains("LoadCacheFromDb", StringComparison.Ordinal));
    }

    // ── Probes ────────────────────────────────────────────────────────────────

    private static async Task<string> VersionAsync(HttpClient client)
    {
        var style = JsonNode.Parse(await client.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;
        return style["metadata"]!["geoassets:version"]!.GetValue<string>();
    }

    private static async Task<string[]> IdsInTileAsync(Host host, string organization)
    {
        using var client = host.ClientFor(organization);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tiles/assets/{await VersionAsync(client)}/{Tile}.mvt", UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal("gzip");

        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var body = new MemoryStream();
        await gzip.CopyToAsync(body);
        var text = Encoding.UTF8.GetString(body.ToArray());
        return [.. new[] { PoleOfA.Id, PoleOfB.Id, SharedPole.Id, "tile-pole-new" }.Where(id => text.Contains(id, StringComparison.Ordinal))];
    }

    private static AssetFeatureMutationItem Attributes(string featureId) => new(
        FeatureId: featureId,
        MutationKind: AssetMutationKind.UpdateAttributes,
        Name: string.Empty,
        AssetTypeId: PoleType,
        Description: string.Empty,
        LayerId: string.Empty,
        Geometry: null,
        TopologyEdges: [],
        CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["occupancyClass"] = "High" });

    // ── Host ──────────────────────────────────────────────────────────────────

    private sealed record Pole(string Id, Guid OrganizationId, double Lon, double Lat);

    private sealed class Host(WebApplication web, RecordingLoggerProvider logs) : IAsyncDisposable
    {
        public IServiceProvider Services => web.Services;

        public MemoryTileCache Cache => (MemoryTileCache)web.Services.GetRequiredService<ITileCache>();

        public ConcurrentQueue<string> Logs => logs.Messages;

        public HttpClient ClientFor(string organization)
        {
            var client = web.GetTestClient();
            client.DefaultRequestHeaders.Add(OrganizationHeader, organization);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await web.StopAsync();
            await web.DisposeAsync();
        }
    }

    /// <summary>A fresh database migrated with the production chain and the three poles; then the production tile wiring.</summary>
    private static async Task<Host> HostAsync(bool rasterTier = false)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"tile_scoping_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
        {
            await geoAssets.Database.MigrateAsync();
        }

        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
        await using (var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
        {
            catalog.AddRange([.. new[] { PoleOfA, PoleOfB, SharedPole }.Select(Feature)]);
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var logs = new RecordingLoggerProvider();
        builder.Logging.AddProvider(logs);
        if (rasterTier)
        {
            // Every layer raster below zoom 16 and vector from it: the raster tiles are the parent of the vector one.
            builder.Configuration["GeoAssets:Tiles:Lod:RasterFromZoom"] = "0";
            builder.Configuration["GeoAssets:Tiles:Lod:VectorFromZoom"] = "16";
        }

        var services = builder.Services;

        // Program.cs's catalog and raster registrations and PostgresSpatialModule's PostGIS ones.
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        services.AddServerOrganizationAssetCatalogs();
        services.AddPostgresAssetRenderQuery(connectionString);
        services.AddSkiaRasterTileSource();
        services.AddPostgresVectorTileSource(connectionString);
        services.AddPostgresAssetMutationBatchApplier(connectionString);
        services.AddTileServing(builder.Configuration);
        services.AddKernelResponseCompression();

        // The caller and their organization, resolved as Program.cs resolves them.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = [OrganizationClaim] }));
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddGeoAuthorizationPolicyBridge();
        services.AddScoped<IGeoAuthorizationService, PermittingAuthorizationService>();
        services.AddSingleton<IOrganizationGrantRepository, NoGrants>();

        var web = builder.Build();
        await web.Services.WarmPostgresAssetProviderAsync();
        web.UseResponseCompression();
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapTileApi();
        await web.StartAsync();
        return new Host(web, logs);
    }

    private static GeoFeature Feature(Pole pole) => new()
    {
        Id = pole.Id,
        Geometry = new GeoPoint(pole.Lon, pole.Lat),
        Properties = new GeoFeatureProperties
        {
            Name = pole.Id,
            AssetTypeId = PoleType,
            OrganizationId = pole.OrganizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
        },
    };

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Recorder(Messages);

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>
    /// Authenticates any request carrying <see cref="OrganizationHeader"/> as a user whose organization claim is the
    /// header's value (none for <see cref="NoOrganization"/>); requests without it stay anonymous.
    /// </summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrganizationHeader, out var organization))
                return Task.FromResult(AuthenticateResult.NoResult());

            List<Claim> claims = [new("oid", "user-1")];
            if (!string.Equals(organization.ToString(), NoOrganization, StringComparison.Ordinal))
                claims.Add(new Claim(OrganizationClaim, organization.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class PermittingAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoGrants : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
