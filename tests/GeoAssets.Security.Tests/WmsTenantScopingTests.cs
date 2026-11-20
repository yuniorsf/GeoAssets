using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.NeutralOperator.Modules;
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
using SkiaSharp;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The WMS endpoint's tenant scoping contract (XD01-372, ADR-001 smell P): anonymous and organization-less callers see
/// the shared features only; an organization's caller sees its organization's features plus the shared ones — the same
/// view as the feature API (XD01-338) — for both painted pixels (GetMap) and GetFeatureInfo. Runs on a database built by
/// the production migrations and with <c>Program.cs</c> + <c>PostgresSpatialModule</c>'s wiring: organization-scoped
/// catalogs, <c>AddPostgresAssetRenderQuery</c>, <see cref="HttpTenantContext"/> and
/// <c>MapWmsApi(requireAuthentication: false)</c> (the <c>Wms:RequireAuthentication</c> default), in the
/// <c>Production</c> environment the deployed stack runs in. The application connects as the tables' owner, as the
/// deployed stack does, so row-level security is not in play and the tenant-stamped transaction plus
/// <see cref="GeoAssetsDbContext"/>'s query filter is the only scoping.
/// </summary>
public sealed class WmsTenantScopingTests
{
    private const string OrganizationClaim = "test_org";
    private const string OrganizationHeader = "X-Test-Organization";
    private const string NoOrganization = "none";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";

    private static readonly Guid OrganizationA = new("a1a1a1a1-0000-4000-8000-000000000355");
    private static readonly Guid OrganizationB = new("b2b2b2b2-0000-4000-8000-000000000355");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // A 1°×1° tile rendered at 200×200 px; each pole sits on its own pixel, 50 px from the others.
    private const double MinLon = -39.0;
    private const double MinLat = -13.5;
    private const double MaxLon = -38.0;
    private const double MaxLat = -12.5;
    private const int TileSize = 200;

    private static readonly Pole PoleOfA = new("wms-pole-org-a", OrganizationA, -38.75, -13.25);
    private static readonly Pole PoleOfB = new("wms-pole-org-b", OrganizationB, -38.50, -13.00);
    private static readonly Pole SharedPole = new("wms-pole-shared", Guid.Empty, -38.25, -12.75);
    private static readonly Pole[] Poles = [PoleOfA, PoleOfB, SharedPole];

    [Fact]
    public async Task Wms_ShowsEachTenantCallerItsOrganizationAndSharedFeatures()
    {
        await using var host = await HostAsync();

        var observed = new Dictionary<string, (string[] FeatureInfo, string[] Painted)>(StringComparer.Ordinal);
        foreach (var (caller, organization) in new (string, string?)[]
                 {
                     ("anonymous", null),
                     ("organization-less", NoOrganization),
                     ("organization A", OrganizationA.ToString()),
                     ("organization B", OrganizationB.ToString()),
                 })
        {
            using var client = host.ClientFor(organization);
            observed[caller] = (await FeatureInfoIdsAsync(client), await PaintedIdsAsync(client));
        }

        var report = string.Join("; ", observed.Select(kv =>
            $"{kv.Key}: GetFeatureInfo=[{string.Join(",", kv.Value.FeatureInfo)}] GetMap=[{string.Join(",", kv.Value.Painted)}]"));

        observed["anonymous"].FeatureInfo.Should().BeEquivalentTo([SharedPole.Id], because: "observed {0}", report);
        observed["anonymous"].Painted.Should().BeEquivalentTo([SharedPole.Id], because: "observed {0}", report);
        observed["organization-less"].FeatureInfo.Should().BeEquivalentTo([SharedPole.Id], because: "observed {0}", report);
        observed["organization-less"].Painted.Should().BeEquivalentTo([SharedPole.Id], because: "observed {0}", report);
        observed["organization A"].FeatureInfo.Should().BeEquivalentTo([PoleOfA.Id, SharedPole.Id], because: "observed {0}", report);
        observed["organization A"].Painted.Should().BeEquivalentTo([PoleOfA.Id, SharedPole.Id], because: "observed {0}", report);
        observed["organization B"].FeatureInfo.Should().BeEquivalentTo([PoleOfB.Id, SharedPole.Id], because: "observed {0}", report);
        observed["organization B"].Painted.Should().BeEquivalentTo([PoleOfB.Id, SharedPole.Id], because: "observed {0}", report);
    }

    // ── Probes ────────────────────────────────────────────────────────────────

    private static async Task<string[]> FeatureInfoIdsAsync(HttpClient client)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pole in Poles)
        {
            var (i, j) = Pixel(pole);
            using var response = await client.GetAsync(WmsUri(
                $"REQUEST=GetFeatureInfo&I={i}&J={j}&FEATURE_COUNT=10&INFO_FORMAT=application/json"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var feature in json.RootElement.GetProperty("features").EnumerateArray())
            {
                ids.Add(feature.GetProperty("id").GetString()!);
            }
        }

        return [.. ids];
    }

    private static async Task<string[]> PaintedIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(WmsUri("REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        return Poles
            .Where(pole =>
            {
                var (i, j) = Pixel(pole);
                return bitmap.GetPixel(i, j).Alpha > 0;
            })
            .Select(pole => pole.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static (int I, int J) Pixel(Pole pole) =>
        ((int)Math.Round((pole.Lon - MinLon) / (MaxLon - MinLon) * TileSize),
         (int)Math.Round((MaxLat - pole.Lat) / (MaxLat - MinLat) * TileSize));

    private static Uri WmsUri(string request) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"/wms?SERVICE=WMS&VERSION=1.1.1&{request}&LAYERS=geoassets&BBOX={MinLon},{MinLat},{MaxLon},{MaxLat}&WIDTH={TileSize}&HEIGHT={TileSize}"),
            UriKind.Relative);

    // ── Host ──────────────────────────────────────────────────────────────────

    private sealed record Pole(string Id, Guid OrganizationId, double Lon, double Lat);

    private sealed class Host(WebApplication web) : IAsyncDisposable
    {
        public HttpClient ClientFor(string? organization)
        {
            var client = web.GetTestClient();
            if (organization is not null)
            {
                client.DefaultRequestHeaders.Add(OrganizationHeader, organization);
            }

            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await web.StopAsync();
            await web.DisposeAsync();
        }
    }

    /// <summary>A fresh database migrated with the production chain and the three poles; then the production WMS wiring.</summary>
    private static async Task<Host> HostAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"wms_scoping_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
        {
            await geoAssets.Database.MigrateAsync();
        }

        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
        await using (var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
        {
            catalog.AddRange([.. Poles.Select(Feature)]);
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;

        // Program.cs's catalog registrations and PostgresSpatialModule's WMS render query.
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        services.AddServerOrganizationAssetCatalogs();
        services.AddPostgresAssetRenderQuery(connectionString);

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
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapWmsApi(requireAuthentication: false);
        await web.StartAsync();
        return new Host(web);
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
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new("oid", "user-1")];
            if (!string.Equals(organization.ToString(), NoOrganization, StringComparison.Ordinal))
            {
                claims.Add(new Claim(OrganizationClaim, organization.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class PermittingAuthorizationService(ITenantContext tenant) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    DisplayName = "User",
                    CreatedAt = SeedTime,
                    OrganizationId = tenant.CurrentOrganizationId == Guid.Empty ? null : tenant.CurrentOrganizationId,
                },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    private sealed class NoGrants : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
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
