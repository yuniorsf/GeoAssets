using System.Diagnostics;
using System.Globalization;
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
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
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
using Xunit.Abstractions;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-458 — the tile versions shared by kernel replicas: two kernel hosts (in-process TestServers wired as the
/// <c>postgres-spatial</c> module wires the tiles, with <c>AddPostgresTileVersionStore</c> + <c>AddTileVersionSharing</c>) over
/// one PostGIS database built by the production migrations. A write through instance 1 evicts instance 2's cached tile and
/// advances the <c>{v}</c> instance 2's <c>style.json</c> gives the writer's organization within <see cref="Bound"/> (the
/// measured delay is written to the test output), while another organization's <c>{v}</c> stays, on both; and a restarted
/// instance starts at the shared epoch and generation — never below what it published before.
/// </summary>
public sealed class TileVersionSharingTests(ITestOutputHelper output)
{
    private const string OrganizationClaim = "test_org";
    private const string OrganizationHeader = "X-Test-Organization";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";
    private const int Zoom = 16;

    /// <summary>The most a change may take to reach the other instance.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static readonly Guid OrganizationA = new("a1a1a1a1-0000-4000-8000-000000000458");
    private static readonly Guid OrganizationB = new("b2b2b2b2-0000-4000-8000-000000000458");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TileAddress Tile = TileAddress.Containing(-38.5, -13.0, Zoom);
    private static readonly (double Lon, double Lat) Center = (
        (Tile.Envelope4326.MinLon + Tile.Envelope4326.MaxLon) / 2, (Tile.Envelope4326.MinLat + Tile.Envelope4326.MaxLat) / 2);

    [Fact]
    public async Task AWriteThroughOneInstance_EvictsTheOthersCachedTile_AndAdvancesItsVersion_WithinABoundedDelay()
    {
        var database = await DatabaseAsync();
        await using var one = await HostAsync(database);
        await using var two = await HostAsync(database);
        await one.WaitSubscribedAsync();
        await two.WaitSubscribedAsync();
        await one.WaitSubscribedAsync(); // Heard after instance 2's startup flush, which it announced first.

        using var twoA = two.ClientFor(OrganizationA);
        using var twoB = two.ClientFor(OrganizationB);
        using var oneB = one.ClientFor(OrganizationB);
        var beforeA = await VersionAsync(twoA);
        var beforeB = await VersionAsync(twoB);
        var beforeOneB = await VersionAsync(oneB);
        (await IdsInTileAsync(twoA)).Should().Equal("tile-pole-a");
        var cached = two.Cache.Count;
        cached.Should().BeGreaterThan(0, "instance 2 cached the tile it drew");

        var waited = Stopwatch.StartNew();
        one.Services.GetRequiredService<IOrganizationCatalogFactory>()
            .GetForOrganization(OrganizationA)
            .Add(Feature("tile-pole-new", OrganizationA, Center.Lon, Center.Lat + 0.0005));
        string afterA;
        while (string.Equals(afterA = await VersionAsync(twoA), beforeA, StringComparison.Ordinal) && waited.Elapsed < Bound)
            await Task.Delay(5);
        var delay = waited.Elapsed;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Instance 2's {{v}} advanced {delay.TotalMilliseconds:F1} ms after instance 1's write ({beforeA} -> {afterA})."));

        afterA.Should().NotBe(beforeA, "instance 2 heard instance 1's write");
        delay.Should().BeLessThan(Bound);
        Generation(afterA).Should().BeGreaterThan(Generation(beforeA));
        (await IdsInTileAsync(twoA)).Should().BeEquivalentTo(["tile-pole-a", "tile-pole-new"], "the tile instance 2 had cached was evicted");
        (await VersionAsync(twoB)).Should().Be(beforeB, "a write in organization A leaves B's {v}");
        (await VersionAsync(oneB)).Should().Be(beforeOneB, "on the writing instance too");
    }

    [Fact]
    public async Task ARestartedInstance_StartsAtTheSharedVersions_NeverBelowWhatItPublished()
    {
        var database = await DatabaseAsync();
        string before;
        string written;
        await using (var first = await HostAsync(database))
        {
            await first.WaitSubscribedAsync();
            using var client = first.ClientFor(OrganizationA);
            before = await VersionAsync(client);
            await first.Services.GetRequiredService<ITileInvalidation>().InvalidateAsync(
                [new DirtyTileRegion(Tile.Envelope4326)], OrganizationA, CancellationToken.None);
            written = await VersionAsync(client);
        }

        await using var restarted = await HostAsync(database);
        using var again = restarted.ClientFor(OrganizationA);
        var after = await VersionAsync(again);

        Generation(written).Should().Be(Generation(before) + 1);
        Epoch(after).Should().Be(Epoch(written), "the epoch is the database's, not the process's");
        Generation(after).Should().Be(Generation(written), "the generation was stored, so it does not start again from zero");
        Flush(after).Should().BeGreaterThan(Flush(written), "a starting instance flushes once, so no URL it serves names older bytes");
    }

    // ── Probes ────────────────────────────────────────────────────────────────

    private static async Task<string> VersionAsync(HttpClient client)
    {
        var style = JsonNode.Parse(await client.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;
        return style["metadata"]!["geoassets:version"]!.GetValue<string>();
    }

    private static long Generation(string version) => long.Parse(version[(version.LastIndexOf('.') + 1)..], CultureInfo.InvariantCulture);

    private static string Epoch(string version) => version.Split('-')[1];

    private static long Flush(string version) => long.Parse(version.Split('-')[2].Split('.')[0], CultureInfo.InvariantCulture);

    private static async Task<string[]> IdsInTileAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tiles/assets/{await VersionAsync(client)}/{Tile}.mvt", UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var body = new MemoryStream();
        await gzip.CopyToAsync(body);
        var text = Encoding.UTF8.GetString(body.ToArray());
        return [.. new[] { "tile-pole-a", "tile-pole-new" }.Where(id => text.Contains(id, StringComparison.Ordinal))];
    }

    // ── Hosts ─────────────────────────────────────────────────────────────────

    private sealed class Host(WebApplication web) : IAsyncDisposable
    {
        public IServiceProvider Services => web.Services;

        public MemoryTileCache Cache => (MemoryTileCache)web.Services.GetRequiredService<ITileCache>();

        public HttpClient ClientFor(Guid organization)
        {
            var client = web.GetTestClient();
            client.DefaultRequestHeaders.Add(OrganizationHeader, organization.ToString());
            return client;
        }

        /// <summary>Waits until this instance hears a probe change announced through the store (its listener is subscribed).</summary>
        public async Task WaitSubscribedAsync()
        {
            var probe = Guid.NewGuid();
            var versions = Services.GetRequiredService<TileVersions>();
            var waited = Stopwatch.StartNew();
            while (!versions.Knows(probe))
            {
                waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the instance subscribes to the shared versions");
                await Services.GetRequiredService<ITileVersionStore>().AdvanceAsync("probe", [probe], flush: false, [], CancellationToken.None);
                await Task.Delay(50);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await web.StopAsync();
            await web.DisposeAsync();
        }
    }

    /// <summary>A fresh database migrated with the production chain, holding organization A's pole and organization B's.</summary>
    private static async Task<string> DatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"tile_versions_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
        {
            await geoAssets.Database.MigrateAsync();
        }

        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
        await using (var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
        {
            catalog.AddRange(
            [
                Feature("tile-pole-a", OrganizationA, Center.Lon - 0.0005, Center.Lat),
                Feature("tile-pole-b", OrganizationB, Center.Lon, Center.Lat),
            ]);
        }

        return connectionString;
    }

    /// <summary>A kernel instance over <paramref name="connectionString"/>: the production tile wiring, sharing its versions.</summary>
    private static async Task<Host> HostAsync(string connectionString)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        services.AddServerOrganizationAssetCatalogs();
        services.AddPostgresVectorTileSource(connectionString);
        services.AddTileServing(builder.Configuration);
        services.AddPostgresTileVersionStore(connectionString);
        services.AddTileVersionSharing();
        services.AddKernelResponseCompression();

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
        return new Host(web);
    }

    private static GeoFeature Feature(string id, Guid organizationId, double lon, double lat) => new()
    {
        Id = id,
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = PoleType,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
        },
    };

    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>Authenticates any request carrying <see cref="OrganizationHeader"/> as a user of that organization.</summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrganizationHeader, out var organization))
                return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", "user-1"), new Claim(OrganizationClaim, organization.ToString())], SchemeName));
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
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsIssuedByAsync(Guid resourceOrganizationId, DateTime utcNow, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
