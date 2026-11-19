using System.Net.Http;
using FluentAssertions;
using GeoAssets.Shared.Services.FeatureActions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.FeatureActions;

/// <summary>Covers <see cref="FeatureActionsServiceExtensions.AddFeatureActionsRest"/> (XD01-370).</summary>
public class FeatureActionsServiceExtensionsTests
{
    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<string> Names { get; } = [];
        public List<HttpClient> Clients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            var client = new HttpClient();
            Clients.Add(client);
            return client;
        }
    }

    private static (ServiceProvider Provider, RecordingHttpClientFactory Factory) Build(Action<IServiceCollection> register)
    {
        var factory = new RecordingHttpClientFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        register(services);
        return (services.BuildServiceProvider(), factory);
    }

    [Theory]
    [InlineData("https://server.example")]
    [InlineData("https://server.example/")]
    [InlineData("https://server.example///")]
    public void AddFeatureActionsRest_BasesTheClientAtTheGeoAssetsApi(string baseUrl)
    {
        var (provider, factory) = Build(s => s.AddFeatureActionsRest(baseUrl));
        using var _ = provider;

        var client = provider.GetRequiredService<IFeatureActionsClient>();

        client.Should().BeOfType<RestFeatureActionsClient>();
        factory.Clients.Should().ContainSingle().Which.BaseAddress
            .Should().Be(new Uri("https://server.example/api/geoassets/"));
    }

    [Fact]
    public void AddFeatureActionsRest_DefaultsToTheGeoAssetsServerNamedClient()
    {
        var (provider, factory) = Build(s => s.AddFeatureActionsRest("https://server.example"));
        using var _ = provider;

        provider.GetRequiredService<IFeatureActionsClient>();

        factory.Names.Should().Equal("GeoAssetsServer");
    }

    [Fact]
    public void AddFeatureActionsRest_HonoursACustomClientName()
    {
        var (provider, factory) = Build(s => s.AddFeatureActionsRest("https://server.example", "Custom"));
        using var _ = provider;

        provider.GetRequiredService<IFeatureActionsClient>();

        factory.Names.Should().Equal("Custom");
    }

    [Fact]
    public void AddFeatureActionsRest_IsScoped()
    {
        var (provider, _) = Build(s => s.AddFeatureActionsRest("https://server.example"));
        using var root = provider;
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<IFeatureActionsClient>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<IFeatureActionsClient>();
        var b = scopeB.ServiceProvider.GetRequiredService<IFeatureActionsClient>();

        a1.Should().BeSameAs(a2);
        a1.Should().NotBeSameAs(b);
    }

    [Fact]
    public void AddFeatureActionsRest_BlankBaseUrl_Throws()
    {
        var act = () => new ServiceCollection().AddFeatureActionsRest(" ");

        act.Should().Throw<ArgumentException>();
    }
}
