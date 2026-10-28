using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.IpBilling.AssetTypes;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.Rest;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

public sealed class IpBillingServerIntegrationTests
{
    [Fact]
    public async Task GetPolesForTransformer_ResolvesIndexLocallyAndQueriesCoreServerOverHttp()
    {
        var (provider, transformerId, pole1Id, pole2Id) = SeedTestNetwork();

        using var coreServer = await BuildCoreServerAsync(provider);
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(
            new Dictionary<(string, string), Guid>
            {
                [("3550308", "INST-001")] = transformerId,
            });

        using var ipBillingServer = await BuildIpBillingServerAsync(coreHttpClient, transformerResolver);
        using var ipBillingClient = ipBillingServer.CreateClient();

        var okResponse = await ipBillingClient.GetAsync(
            new Uri("/api/ip-billing/transformers/3550308/INST-001/poles", UriKind.Relative));
        okResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var poles = await okResponse.Content.ReadFromJsonAsync<List<Guid>>();
        poles.Should().NotBeNull();
        poles.Should().BeEquivalentTo([pole1Id, pole2Id]);

        var missingResponse = await ipBillingClient.GetAsync(
            new Uri("/api/ip-billing/transformers/3550308/UNKNOWN/poles", UriKind.Relative));
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static (IAssetProvider Provider, Guid TransformerId, Guid Pole1Id, Guid Pole2Id) SeedTestNetwork()
    {
        var provider = new TestAssetProvider();
        provider.AddAssetType(IpBillingAssetTypes.Transformador);
        provider.AddAssetType(IpBillingAssetTypes.Poste);

        var transformerId = Guid.NewGuid();
        var pole1Id = Guid.NewGuid();
        var pole2Id = Guid.NewGuid();

        provider.Add(new GeoFeature
        {
            Id = transformerId.ToString(),
            Geometry = new GeoPoint(-46.63, -23.55),
            Properties = new GeoFeatureProperties
            {
                Name = "TR-SP-01",
                AssetTypeId = IpBillingAssetTypes.Transformador.Id.ToString(),
            },
            Topology =
            [
                new TopoEdge { TargetId = pole1Id.ToString(), Kind = "feeds" },
                new TopoEdge { TargetId = pole2Id.ToString(), Kind = "feeds" },
            ],
        });
        provider.Add(new GeoFeature
        {
            Id = pole1Id.ToString(),
            Geometry = new GeoPoint(-46.631, -23.551),
            Properties = new GeoFeatureProperties
            {
                Name = "P-SP-01",
                AssetTypeId = IpBillingAssetTypes.Poste.Id.ToString(),
            },
        });
        provider.Add(new GeoFeature
        {
            Id = pole2Id.ToString(),
            Geometry = new GeoPoint(-46.632, -23.552),
            Properties = new GeoFeatureProperties
            {
                Name = "P-SP-02",
                AssetTypeId = IpBillingAssetTypes.Poste.Id.ToString(),
            },
        });

        return (provider, transformerId, pole1Id, pole2Id);
    }

    private static async Task<TestServer> BuildCoreServerAsync(IAssetProvider provider)
    {
        var host = await new HostBuilder()
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
                    services.AddSingleton<IGeoAuthorizationService>(new AllowAllAuthorizationService());
                    services.AddSingleton<IOrganizationGrantRepository, NoOpGrantRepository>();
                    services.AddSingleton(provider);
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NoOpDbContextFactory>();
                    services.AddSingleton<WmsPostGisRenderer>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static async Task<TestServer> BuildIpBillingServerAsync(
        HttpClient coreHttpClient,
        ITransformerResolver transformerResolver)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IGeoCoreClient>(new RestGeoCoreClient(coreHttpClient));
                    services.AddSingleton(transformerResolver);
                    services.AddScoped<IPoleResolver, HttpPoleResolver>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapIpBillingApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private sealed class StubTransformerResolver(
        IReadOnlyDictionary<(string IbgeCode, string CodigoInstalacao), Guid> mappings)
        : ITransformerResolver
    {
        public Task<Guid?> ResolveTransformerFeatureIdAsync(
            string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default) =>
            Task.FromResult(
                mappings.TryGetValue((municipioIbgeCode, codigoInstalacao), out var id)
                    ? (Guid?)id
                    : null);
    }

    private sealed class AllowAllAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpDbContextFactory : IDbContextFactory<GeoAssetsDbContext>
    {
        public GeoAssetsDbContext CreateDbContext() => throw new NotSupportedException();
        public Task<GeoAssetsDbContext> CreateDbContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpGrantRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
