using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-330 — <c>GET /api/geoassets/organizations/{organizationId}/reference-features</c> through the real route
/// (<see cref="GeoAssetsRestApiExtensions.MapGeoAssetsApi"/>), the real decision (<see cref="OrganizationResourceAccess"/>)
/// and the real grant repository (<c>EFOrganizationGrantRepository</c>, so revoked and expired grants are the store's own
/// answer). COELBA grants BIMBO <c>features:reference</c>; the caller is a BIMBO user. The read is the generic
/// <see cref="OrganizationCatalogReferenceFeatureQuery"/> over an in-memory catalog holding three organizations' features,
/// recorded so a test can prove nothing is read before access is decided.
/// </summary>
public sealed class ReferenceFeaturesEndpointTests : IAsyncLifetime, IDisposable
{
    private const string Reference = "features:reference";
    private const string Read = "features:read";
    private const string Municipio1 = "municipio-1";
    private const string Bbox = "bbox=0,0,10,10";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000330");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000330");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000330");
    private static readonly Guid Empty = new("0dd00000-0000-4000-8000-000000000330");
    private static readonly Guid CoelbaPole = new("c0e1ba00-0001-4000-8000-000000000330");

    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] TheFiveFields = ["id", "geometry", "label", "assetTypeId", "assetTypeName"];

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly TestAssetProvider _catalog = new();
    private GeoIdentityDbContext _identity = null!;
    private RecordingReferenceFeatureQuery _query = null!;
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _identity = new GeoIdentityDbContext(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);
        await _identity.Database.EnsureCreatedAsync();
        _query = new RecordingReferenceFeatureQuery(new OrganizationCatalogReferenceFeatureQuery(new FilteringOrganizationCatalogFactory(_catalog)));

        _catalog.AddAssetType(new AssetType { Id = CoelbaPole, Name = "COELBA pole", OrganizationId = Coelba });
        Seed("coelba-m1", Coelba, new GeoPoint(1, 1), Municipio1);
        Seed("coelba-m2", Coelba, new GeoPoint(2, 2), "municipio-2");
        Seed("coelba-unscoped", Coelba, new GeoPoint(3, 3));
        Seed("coelba-outside", Coelba, new GeoPoint(50, 50), Municipio1);
        Seed("bimbo-1", Bimbo, new GeoPoint(4, 4));
        Seed("third-1", Third, new GeoPoint(5, 5), Municipio1);
        Seed("shared-1", Guid.Empty, new GeoPoint(6, 6));
    }

    public Task DisposeAsync() => StopHostAsync();

    public void Dispose()
    {
        _identity.Dispose();
        _connection.Dispose();
    }

    private async Task StopHostAsync()
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    private void Seed(string id, Guid owner, GeoGeometry geometry, string? scopeId = null)
    {
        var properties = new GeoFeatureProperties
        {
            Name = $"Label {id}",
            AssetTypeId = CoelbaPole.ToString(),
            OrganizationId = owner,
            Description = "internal description",
        };
        properties.CustomAttributes["plate"] = $"secret-{id}";
        if (scopeId is not null) properties.CustomAttributes["scopeId"] = scopeId;
        _catalog.Add(new GeoFeature
        {
            Id = id,
            Geometry = geometry,
            Properties = properties,
            Topology = { new TopoEdge { TargetId = "elsewhere", Kind = "feeds" } },
        });
    }

    private Task<int> GrantAsync(
        Guid from, string[]? actions = null, string? scopeId = Municipio1, bool requiresScopedRole = true,
        bool active = true, DateTime? expiresAt = null, string? resourceType = nameof(GeoFeature), string? requiredRole = null)
    {
        _identity.OrganizationGrants.Add(new OrganizationGrant
        {
            GranteeOrganizationId = Bimbo,
            ResourceOrganizationId = from,
            ResourceType = resourceType,
            ScopeId = scopeId,
            AllowedActions = [.. actions ?? [Reference]],
            RequiredRole = requiredRole,
            RequiresScopedRole = requiresScopedRole,
            GrantedBy = "admin@coelba.test",
            GrantedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt,
            IsActive = active,
        });
        return _identity.SaveChangesAsync();
    }

    /// <summary>A BIMBO caller: <paramref name="home"/> permissions at home, <paramref name="onCoelba"/> through a role on COELBA.</summary>
    private async Task<HttpClient> ClientAsync(string[]? home = null, string[]? onCoelba = null, string[]? rolesOnCoelba = null)
    {
        var authorization = new CallerAuthorization(home ?? [], onCoelba ?? [], rolesOnCoelba ?? ["Contractor C1"]);

        await StopHostAsync();
        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<IGeoAuthorizationService>(authorization);
                    services.AddSingleton<IOrganizationGrantRepository>(new EFOrganizationGrantRepository(_identity, _time));
                    services.AddSingleton<IReferenceFeatureQuery>(_query);
                    services.AddSingleton<IAssetProvider>(new TestAssetProvider());
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NeverCalledDbContextFactory>();
                    services.AddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "tech@bimbo.test")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
                });
            })
            .StartAsync();

        var client = _host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    private static Uri Route(Guid organizationId, string query = Bbox) =>
        new($"/api/geoassets/organizations/{organizationId}/reference-features?{query}", UriKind.Relative);

    private static async Task<IReadOnlyList<string>> IdsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];
    }

    // ── Entitled callers ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_caller_with_the_scoped_permission_gets_the_features_of_the_grant_scope_with_only_the_five_fields()
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        var response = await client.GetAsync(Route(Coelba));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.Private.Should().BeTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(TheFiveFields);
        item.GetProperty("id").GetString().Should().Be("coelba-m1");
        item.GetProperty("label").GetString().Should().Be("Label coelba-m1");
        item.GetProperty("assetTypeId").GetString().Should().Be(CoelbaPole.ToString());
        item.GetProperty("assetTypeName").GetString().Should().Be("COELBA pole");
        item.GetProperty("geometry").GetProperty("type").GetString().Should().Be("Point");
        item.GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble()).Should().Equal(1, 1);
        (await response.Content.ReadAsStringAsync()).Should().NotContainAny("secret-", "internal description", "elsewhere", Coelba.ToString());
    }

    [Fact]
    public async Task Features_outside_the_grants_scope_are_not_returned_and_an_unscoped_grant_returns_every_feature_in_the_box()
    {
        await GrantAsync(Coelba);
        await GrantAsync(Coelba, scopeId: null, requiresScopedRole: false);

        using var scoped = await ClientAsync(home: [], onCoelba: [Reference]);
        (await IdsAsync(await scoped.GetAsync(Route(Coelba)))).Should().Equal("coelba-m1");

        using var organizationWide = await ClientAsync(home: [Reference], onCoelba: []);
        (await IdsAsync(await organizationWide.GetAsync(Route(Coelba)))).Should().Equal("coelba-m1", "coelba-m2", "coelba-unscoped");
    }

    [Fact]
    public async Task The_route_does_not_require_the_home_features_read_permission()
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        (await client.GetAsync(Route(Coelba))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_asset_type_filter_and_the_scopes_reach_the_query()
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        var response = await client.GetAsync(Route(Coelba, $"{Bbox}&assetTypeId=other-type"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IdsAsync(response)).Should().BeEmpty();
        var call = _query.Calls.Should().ContainSingle().Subject;
        call.OrganizationId.Should().Be(Coelba);
        call.Bbox.Should().Be(new BoundingBox(0, 0, 10, 10));
        call.Filter.AssetTypeId.Should().Be("other-type");
        call.Filter.ScopeIds.Should().Equal(Municipio1);
        call.Filter.MaxResults.Should().Be(10_000);
    }

    [Fact]
    public async Task The_response_is_capped_like_the_bounds_route()
    {
        await GrantAsync(Coelba, scopeId: null);
        for (var i = 0; i <= 10_000; i++)
            Seed($"bulk-{i:D5}", Coelba, new GeoPoint(7 + (i % 100) * 0.01, 7 + (i / 100) * 0.01));
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        var ids = await IdsAsync(await client.GetAsync(Route(Coelba)));

        ids.Should().HaveCount(10_000);
    }

    // ── Refused callers: 403, decided before anything is read ────────────────

    public static TheoryData<string> RefusedCases() =>
    [
        "no grant",
        "scoped grant, permission held at home only",
        "scoped grant, other permission held on the owner",
        "organization-wide grant, permission held on the owner only",
        "grant listing features:read only",
        "grant for another resource type",
        "revoked grant",
        "expired grant",
        "required role not held on the owner",
    ];

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public async Task A_caller_without_a_matching_grant_or_the_scoped_permission_gets_403_and_nothing_is_read(string refusal)
    {
        HttpClient client;
        switch (refusal)
        {
            case "no grant":
                client = await ClientAsync(home: [Reference, Read], onCoelba: [Reference]);
                break;
            case "scoped grant, permission held at home only":
                await GrantAsync(Coelba);
                client = await ClientAsync(home: [Reference, Read], onCoelba: []);
                break;
            case "scoped grant, other permission held on the owner":
                await GrantAsync(Coelba);
                client = await ClientAsync(home: [], onCoelba: [Read]);
                break;
            case "organization-wide grant, permission held on the owner only":
                await GrantAsync(Coelba, requiresScopedRole: false);
                client = await ClientAsync(home: [], onCoelba: [Reference]);
                break;
            case "grant listing features:read only":
                await GrantAsync(Coelba, actions: [Read]);
                client = await ClientAsync(home: [Read], onCoelba: [Read, Reference]);
                break;
            case "grant for another resource type":
                await GrantAsync(Coelba, resourceType: "ServiceOrder");
                client = await ClientAsync(home: [], onCoelba: [Reference]);
                break;
            case "revoked grant":
                await GrantAsync(Coelba, active: false);
                client = await ClientAsync(home: [], onCoelba: [Reference]);
                break;
            case "expired grant":
                await GrantAsync(Coelba, expiresAt: Now.AddMinutes(-1));
                client = await ClientAsync(home: [], onCoelba: [Reference]);
                break;
            default:
                await GrantAsync(Coelba, requiredRole: "Contractor C1");
                client = await ClientAsync(home: [], onCoelba: [Reference], rolesOnCoelba: ["Viewer"]);
                break;
        }

        using (client)
        {
            var response = await client.GetAsync(Route(Coelba));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            response.Headers.CacheControl!.Private.Should().BeTrue();
        }

        _query.Calls.Should().BeEmpty("access is decided before any data is read");
    }

    [Fact]
    public async Task A_grant_that_has_not_expired_yet_still_counts()
    {
        await GrantAsync(Coelba, expiresAt: Now.AddMinutes(1));
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        (await client.GetAsync(Route(Coelba))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_caller_entitled_on_COELBA_cannot_read_a_third_organization_by_changing_the_id()
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [Reference, Read], onCoelba: [Reference]);

        var third = await client.GetAsync(Route(Third));
        var empty = await client.GetAsync(Route(Empty));

        third.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        empty.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await third.Content.ReadAsStringAsync()).Should().Be(await empty.Content.ReadAsStringAsync(),
            "the refusal must not reveal whether the organization has data");
        _query.Calls.Should().BeEmpty();
    }

    // ── Request validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await GrantAsync(Coelba);
        await ClientAsync(home: [], onCoelba: [Reference]);
        using var anonymous = _host!.GetTestServer().CreateClient();

        (await anonymous.GetAsync(Route(Coelba))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bbox=")]
    [InlineData("assetTypeId=x")]
    [InlineData("bbox=0,0,10")]
    [InlineData("bbox=0,0,10,10,5")]
    [InlineData("bbox=a,0,10,10")]
    [InlineData("bbox=0,0,NaN,10")]
    [InlineData("bbox=0,0,Infinity,10")]
    [InlineData("bbox=10,0,0,10")]
    [InlineData("bbox=0,10,10,0")]
    [InlineData("bbox=-181,0,10,10")]
    [InlineData("bbox=0,-91,10,10")]
    [InlineData("bbox=0,0,181,10")]
    [InlineData("bbox=0,0,10,91")]
    public async Task A_missing_or_invalid_bounding_box_is_400(string query)
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        (await client.GetAsync(Route(Coelba, query))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _query.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-180,-90,180,90", -180, -90, 180, 90)]
    [InlineData("-38.5,-12.9,-38.4,-12.8", -38.5, -12.9, -38.4, -12.8)]
    [InlineData("1,1,1,1", 1, 1, 1, 1)]
    [InlineData(" 1, 2 ,3,4", 1, 2, 3, 4)]
    public async Task Valid_bounding_boxes_are_read_in_RFC_7946_order(string raw, double minLon, double minLat, double maxLon, double maxLat)
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);

        (await client.GetAsync(Route(Coelba, $"bbox={Uri.EscapeDataString(raw)}"))).StatusCode.Should().Be(HttpStatusCode.OK);

        _query.Calls.Should().ContainSingle().Which.Bbox.Should().Be(new BoundingBox(minLon, minLat, maxLon, maxLat));
    }

    [Fact]
    public async Task Bounding_boxes_are_read_with_the_invariant_culture()
    {
        await GrantAsync(Coelba);
        using var client = await ClientAsync(home: [], onCoelba: [Reference]);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            (await client.GetAsync(Route(Coelba, "bbox=0.5,0.5,1.5,1.5"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        _query.Calls.Should().ContainSingle().Which.Bbox.Should().Be(new BoundingBox(0.5, 0.5, 1.5, 1.5));
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class CallerAuthorization(string[] home, string[] onCoelba, string[] rolesOnCoelba) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(home.Contains(permissionCode, StringComparer.OrdinalIgnoreCase));

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(), Email = "tech@bimbo.test", DisplayName = "Tech",
                    CreatedAt = Now, OrganizationId = Bimbo,
                },
                Roles = [],
                Claims = [],
                Permissions = home,
                ScopedRoles = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = rolesOnCoelba },
                ScopedPermissions = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = onCoelba },
            });
    }

    private sealed class RecordingReferenceFeatureQuery(IReferenceFeatureQuery inner) : IReferenceFeatureQuery
    {
        public List<(Guid OrganizationId, BoundingBox Bbox, ReferenceFeatureFilter Filter)> Calls { get; } = [];

        public Task<IReadOnlyList<ReferenceFeature>> GetInBoundsAsync(
            Guid organizationId, BoundingBox bbox, ReferenceFeatureFilter filter, CancellationToken ct)
        {
            Calls.Add((organizationId, bbox, filter));
            return inner.GetInBoundsAsync(organizationId, bbox, filter, ct);
        }
    }
}
