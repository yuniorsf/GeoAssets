using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// The tile endpoints on a test host wired as the <c>postgres-spatial</c> module and <c>Program.cs</c> wire them —
/// <c>AddTileServing</c>, response compression, authentication/authorization (<c>features:read</c> through the policy
/// bridge), the organization catalog warm-up middleware, <c>MapTileApi</c> — over recording tile sources, a catalog, and a
/// caller named by headers: <see cref="OrganizationHeader"/> (its organization; none = anonymous, <see cref="NoOrganization"/>
/// = an organization-less user), <see cref="DenyHeader"/> (without <c>features:read</c>) and <see cref="AdminHeader"/> (with
/// <c>organizations:edit</c>, XD01-439) — and a <see cref="ScriptedDensitySampler"/> as the LOD calibration's sampler.
/// </summary>
internal sealed class TileTestHost : IAsyncDisposable
{
    public const string OrganizationHeader = "X-Test-Organization";
    public const string DenyHeader = "X-Test-Deny";
    public const string AdminHeader = "X-Test-Admin";
    public const string NoOrganization = "none";
    public const string Epoch = "cafe0001";

    private readonly WebApplication _web;

    private TileTestHost(WebApplication web, TestAssetProvider catalog)
    {
        _web = web;
        Catalog = catalog;
    }

    public TestAssetProvider Catalog { get; }

    public IServiceProvider Services => _web.Services;

    public RecordingVectorSource Vector => _web.Services.GetRequiredService<RecordingVectorSource>();

    public RecordingRasterSource Raster => _web.Services.GetRequiredService<RecordingRasterSource>();

    public CountingCatalogFactory Catalogs => _web.Services.GetRequiredService<CountingCatalogFactory>();

    public Grants Grants => _web.Services.GetRequiredService<Grants>();

    public ScriptedDensitySampler Sampler => _web.Services.GetRequiredService<ScriptedDensitySampler>();

    public TileServing Serving => _web.Services.GetRequiredService<TileServing>();

    public TileVersions Versions => _web.Services.GetRequiredService<TileVersions>();

    public ITileInvalidation Invalidation => _web.Services.GetRequiredService<ITileInvalidation>();

    public MemoryTileCache Cache => (MemoryTileCache)_web.Services.GetRequiredService<ITileCache>();

    public static Task<TileTestHost> StartAsync(IEnumerable<AssetType> assetTypes, params (string Key, string Value)[] settings) =>
        StartAsync(assetTypes, _ => { }, settings);

    /// <summary>A host whose services <paramref name="configure"/> adds to (e.g. keyed tile sources, XD01-459) before the tiles' own.</summary>
    public static async Task<TileTestHost> StartAsync(
        IEnumerable<AssetType> assetTypes, Action<IServiceCollection> configure, params (string Key, string Value)[] settings)
    {
        var catalog = new TestAssetProvider(assetTypes);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));

        var services = builder.Services;
        services.AddSingleton<IAssetProvider>(catalog);
        services.AddScoped<ITenantContext, HeaderTenant>();
        services.AddHttpContextAccessor();
        services.AddSingleton<Grants>();
        services.AddSingleton<IOrganizationGrantRepository>(sp => sp.GetRequiredService<Grants>());
        services.AddSingleton<CountingCatalogFactory>();
        services.AddSingleton<IOrganizationCatalogFactory>(sp => sp.GetRequiredService<CountingCatalogFactory>());
        services.AddSingleton<RecordingVectorSource>();
        services.AddSingleton<IVectorTileSource>(sp => sp.GetRequiredService<RecordingVectorSource>());
        services.AddSingleton<RecordingRasterSource>();
        services.AddSingleton<IRasterTileSource>(sp => sp.GetRequiredService<RecordingRasterSource>());
        services.AddSingleton<ScriptedDensitySampler>();
        services.AddSingleton<ITileDensitySampler>(sp => sp.GetRequiredService<ScriptedDensitySampler>());
        configure(services);
        services.AddSingleton(new TileVersions(Epoch));
        services.AddTileServing(builder.Configuration);
        services.AddKernelResponseCompression();
        services.AddAuthentication(HeaderAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddGeoAuthorizationPolicyBridge();
        services.AddScoped<IGeoAuthorizationService, HeaderAuthorization>();

        var web = builder.Build();
        web.UseResponseCompression();
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapTileApi();
        web.MapGet("/warmed", () => "ok");
        await web.StartAsync();
        return new TileTestHost(web, catalog);
    }

    /// <summary>A client calling as a user of <paramref name="organization"/> (<c>null</c> = anonymous).</summary>
    public HttpClient ClientFor(Guid? organization, bool withFeaturesRead = true, bool organizationLess = false, bool admin = false)
    {
        var client = _web.GetTestClient();
        if (admin) client.DefaultRequestHeaders.Add(AdminHeader, "1");
        if (organizationLess) client.DefaultRequestHeaders.Add(OrganizationHeader, NoOrganization);
        else if (organization is { } id) client.DefaultRequestHeaders.Add(OrganizationHeader, id.ToString());
        if (!withFeaturesRead) client.DefaultRequestHeaders.Add(DenyHeader, "1");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _web.StopAsync();
        await _web.DisposeAsync();
    }

    private sealed class HeaderTenant(IHttpContextAccessor accessor) : ITenantContext
    {
        public Guid CurrentOrganizationId =>
            accessor.HttpContext?.User.FindFirst("test_org")?.Value is { } org ? Guid.Parse(org) : Guid.Empty;
    }

    private sealed class HeaderAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TileTestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrganizationHeader, out var organization))
                return Task.FromResult(AuthenticateResult.NoResult());

            List<Claim> claims = [new("oid", "user-1")];
            if (!string.Equals(organization.ToString(), NoOrganization, StringComparison.Ordinal))
                claims.Add(new Claim("test_org", organization.ToString()));
            if (Request.Headers.ContainsKey(DenyHeader))
                claims.Add(new Claim("test_deny", "1"));
            if (Request.Headers.ContainsKey(AdminHeader))
                claims.Add(new Claim("test_admin", "1"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class HeaderAuthorization(IHttpContextAccessor accessor) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
        {
            var user = accessor.HttpContext?.User;
            return Task.FromResult(permissionCode switch
            {
                "features:read" => user?.FindFirst("test_deny") is null,
                TileLodCalibrationEndpoint.AdminPermission => user?.FindFirst("test_admin") is not null,
                _ => false,
            });
        }

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
