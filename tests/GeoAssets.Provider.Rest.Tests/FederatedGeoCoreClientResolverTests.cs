using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

public sealed class FederatedGeoCoreClientResolverTests
{
    private static readonly Guid CoelbaOrgId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid EquatorialOrgId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid AguasOrgId = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid UnmappedOrgId = Guid.Parse("99999999-0000-0000-0000-000000000099");

    [Theory]
    [InlineData("electric-coelba", "pole-ba-101", "coelba.geoassets.internal")]
    [InlineData("electric-equatorial", "pole-ma-202", "equatorial.geoassets.internal")]
    [InlineData("sanitation-aguas", "pipe-rj-303", "aguas.geoassets.internal")]
    public async Task Resolve_ByExplicitCatalogInstanceId_RoutesDirectHttpReadToExpectedGeoCoreInstance(
        string catalogInstanceId,
        string featureId,
        string expectedHost)
    {
        using var handler = new RecordingHttpMessageHandler(req =>
        {
            var feature = new GeoFeature
            {
                Id = featureId,
                Geometry = new GeoPoint(-38.5014, -12.9714),
                Properties = new GeoFeatureProperties
                {
                    Name = $"ServedBy:{req.RequestUri!.Host}",
                    AssetTypeId = "network-asset"
                }
            };
            return JsonResponse(GeoJsonSerializer.SerializeFeature(feature));
        });
        var factory = new FakeHttpClientFactory(handler);
        var resolver = new FederatedGeoCoreClientResolver(factory, Options.Create(CreateThreeCatalogOptions()));

        resolver.RegisteredCatalogIds.Should().BeEquivalentTo(
            ["electric-coelba", "electric-equatorial", "sanitation-aguas"]);

        var client = resolver.Resolve(catalogInstanceId);
        var loaded = await client.GetFeatureByIdAsync(featureId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(featureId);
        loaded.Properties.Name.Should().Be($"ServedBy:{expectedHost}");
        handler.RecordedUris.Should().ContainSingle()
            .Which.Host.Should().Be(expectedHost);
    }

    [Fact]
    public async Task Resolve_ByOrganizationIdWhenCatalogOmitted_RoutesToMappedCatalogShard()
    {
        using var handler = new RecordingHttpMessageHandler(req =>
        {
            var feature = new GeoFeature
            {
                Id = "hydrant-1",
                Geometry = new GeoPoint(-43.1729, -22.9068),
                Properties = new GeoFeatureProperties { Name = req.RequestUri!.Host }
            };
            return JsonResponse(GeoJsonSerializer.SerializeFeature(feature));
        });
        var resolver = new FederatedGeoCoreClientResolver(
            new FakeHttpClientFactory(handler),
            Options.Create(CreateThreeCatalogOptions()));

        var aguasClient = resolver.Resolve(catalogInstanceId: null, organizationId: AguasOrgId);
        var equatorialClient = resolver.Resolve(catalogInstanceId: null, organizationId: EquatorialOrgId);

        var aguasFeature = await aguasClient.GetFeatureByIdAsync("hydrant-1");
        var equatorialFeature = await equatorialClient.GetFeatureByIdAsync("hydrant-1");

        aguasFeature!.Properties.Name.Should().Be("aguas.geoassets.internal");
        equatorialFeature!.Properties.Name.Should().Be("equatorial.geoassets.internal");
    }

    [Fact]
    public async Task Resolve_DefaultFallback_UsesConfiguredDefaultCatalogWhenNoCatalogOrMappedOrgProvided()
    {
        using var handler = new RecordingHttpMessageHandler(req =>
        {
            var feature = new GeoFeature
            {
                Id = "default-feat",
                Properties = new GeoFeatureProperties { Name = req.RequestUri!.Host }
            };
            return JsonResponse(GeoJsonSerializer.SerializeFeature(feature));
        });
        var resolver = new FederatedGeoCoreClientResolver(
            new FakeHttpClientFactory(handler),
            Options.Create(CreateThreeCatalogOptions()));

        var defaultClient = resolver.Resolve();
        var unmappedOrgClient = resolver.Resolve(catalogInstanceId: null, organizationId: UnmappedOrgId);

        (await defaultClient.GetFeatureByIdAsync("default-feat"))!.Properties.Name
            .Should().Be("coelba.geoassets.internal");
        (await unmappedOrgClient.GetFeatureByIdAsync("default-feat"))!.Properties.Name
            .Should().Be("coelba.geoassets.internal");
    }

    [Fact]
    public void Resolve_ExplicitUnknownCatalogInstanceId_FailsClosedEvenWhenOrganizationIsMapped()
    {
        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var resolver = new FederatedGeoCoreClientResolver(
            new FakeHttpClientFactory(handler),
            Options.Create(CreateThreeCatalogOptions()));

        var found = resolver.TryResolve("unknown-catalog-shard", CoelbaOrgId, out var client);
        found.Should().BeFalse();
        client.Should().BeNull();

        var act = () => resolver.Resolve("unknown-catalog-shard", CoelbaOrgId);
        var ex = act.Should().Throw<Exception>().Which;
        (ex is KeyNotFoundException or InvalidOperationException or ArgumentException).Should().BeTrue();
        handler.RecordedUris.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DuplicateOrganizationAcrossCatalogs_FailsValidationAtStartup()
    {
        var sharedOrgId = Guid.Parse("77777777-0000-0000-0000-000000000077");
        var options = CreateThreeCatalogOptions();
        options.Catalogs["electric-coelba"].OrganizationIds.Add(sharedOrgId);
        options.Catalogs["sanitation-aguas"].OrganizationIds.Add(sharedOrgId);

        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var act = () => new FederatedGeoCoreClientResolver(
            new FakeHttpClientFactory(handler),
            Options.Create(options));

        var ex = act.Should().Throw<Exception>().Which;
        (ex is InvalidOperationException or ArgumentException or OptionsValidationException).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/relative/api/geoassets")]
    [InlineData("not-a-valid-uri")]
    public void Constructor_InvalidCatalogBaseUrl_FailsValidationAtStartup(string invalidBaseUrl)
    {
        var options = new GeoCoreFederationOptions
        {
            DefaultCatalogInstanceId = "electric-coelba",
            Catalogs = new Dictionary<string, GeoCoreCatalogEndpoint>(StringComparer.Ordinal)
            {
                ["electric-coelba"] = new GeoCoreCatalogEndpoint
                {
                    CatalogInstanceId = "electric-coelba",
                    BaseUrl = invalidBaseUrl,
                    OrganizationIds = [CoelbaOrgId]
                }
            }
        };

        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var act = () => new FederatedGeoCoreClientResolver(
            new FakeHttpClientFactory(handler),
            Options.Create(options));

        var ex = act.Should().Throw<Exception>().Which;
        (ex is InvalidOperationException or ArgumentException or UriFormatException or OptionsValidationException)
            .Should().BeTrue();
    }

    [Fact]
    public async Task DependencyInjection_AddFederatedGeoCoreRestClients_AndBackwardsCompatibleAddGeoCoreRestClient_Work()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{GeoCoreFederationOptions.SectionName}:DefaultCatalogInstanceId"] = "electric-coelba",
            [$"{GeoCoreFederationOptions.SectionName}:Catalogs:electric-coelba:CatalogInstanceId"] = "electric-coelba",
            [$"{GeoCoreFederationOptions.SectionName}:Catalogs:electric-coelba:BaseUrl"] = "https://coelba.geoassets.internal/api/geoassets",
            [$"{GeoCoreFederationOptions.SectionName}:Catalogs:sanitation-aguas:CatalogInstanceId"] = "sanitation-aguas",
            [$"{GeoCoreFederationOptions.SectionName}:Catalogs:sanitation-aguas:BaseUrl"] = "https://aguas.geoassets.internal/api/geoassets",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        using var handler = new RecordingHttpMessageHandler(req =>
        {
            var feature = new GeoFeature
            {
                Id = "f-di",
                Properties = new GeoFeatureProperties { Name = req.RequestUri!.Host }
            };
            return JsonResponse(GeoJsonSerializer.SerializeFeature(feature));
        });

        var federatedServices = new ServiceCollection();
        federatedServices.AddFederatedGeoCoreRestClients(configuration);
        federatedServices.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(handler));

        await using (var provider = federatedServices.BuildServiceProvider())
        {
            var resolver = provider.GetRequiredService<IGeoCoreClientResolver>();
            resolver.RegisteredCatalogIds.Should().Contain(["electric-coelba", "sanitation-aguas"]);

            var defaultClient = provider.GetRequiredService<IGeoCoreClient>();
            var defaultFeature = await defaultClient.GetFeatureByIdAsync("f-di");
            defaultFeature!.Properties.Name.Should().Be("coelba.geoassets.internal");

            var aguasFeature = await resolver.Resolve("sanitation-aguas").GetFeatureByIdAsync("f-di");
            aguasFeature!.Properties.Name.Should().Be("aguas.geoassets.internal");
        }

        var legacyServices = new ServiceCollection();
        legacyServices.AddGeoCoreRestClient("https://legacy.geoassets.internal/api/geoassets");
        legacyServices.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(handler));

        await using (var legacyProvider = legacyServices.BuildServiceProvider())
        {
            var legacyClient = legacyProvider.GetRequiredService<IGeoCoreClient>();
            var legacyFeature = await legacyClient.GetFeatureByIdAsync("f-di");
            legacyFeature!.Properties.Name.Should().Be("legacy.geoassets.internal");
        }
    }

    private static GeoCoreFederationOptions CreateThreeCatalogOptions() =>
        new()
        {
            DefaultCatalogInstanceId = "electric-coelba",
            Catalogs = new Dictionary<string, GeoCoreCatalogEndpoint>(StringComparer.Ordinal)
            {
                ["electric-coelba"] = new GeoCoreCatalogEndpoint
                {
                    CatalogInstanceId = "electric-coelba",
                    BaseUrl = "https://coelba.geoassets.internal/api/geoassets",
                    OrganizationIds = [CoelbaOrgId]
                },
                ["electric-equatorial"] = new GeoCoreCatalogEndpoint
                {
                    CatalogInstanceId = "electric-equatorial",
                    BaseUrl = "https://equatorial.geoassets.internal/api/geoassets",
                    OrganizationIds = [EquatorialOrgId]
                },
                ["sanitation-aguas"] = new GeoCoreCatalogEndpoint
                {
                    CatalogInstanceId = "sanitation-aguas",
                    BaseUrl = "https://aguas.geoassets.internal/api/geoassets",
                    OrganizationIds = [AguasOrgId]
                }
            }
        };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> RecordedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            if (request.RequestUri is not null)
            {
                RecordedUris.Add(request.RequestUri);
            }

            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

