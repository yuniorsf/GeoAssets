using Xunit;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions.Tests;

public sealed class GeoAssetsModuleHostingExtensionsTests
{
    private static WebApplicationBuilder NewBuilder(params string[] enabled)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            enabled.Select((id, i) => new KeyValuePair<string, string?>($"{GeoAssetsModuleHostingExtensions.ModulesSectionKey}:{i}", id)));
        return builder;
    }

    private static RecordingModule Serving(string id, string route, string body, List<string>? calls = null) =>
        new(id, calls, map: endpoints => ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)endpoints).MapGet(route, () => body));

    [Fact]
    public void AddGeoAssetsModules_ConfiguresOnlyEnabledModules_InConfiguredOrder()
    {
        var calls = new List<string>();
        var builder = NewBuilder("b", "a");

        builder.AddGeoAssetsModules(new RecordingModule("a", calls), new RecordingModule("b", calls), new RecordingModule("c", calls));
        using var app = builder.Build();

        calls.Should().Equal("configure:b", "configure:a");
        app.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Select(m => m.Id).Should().Equal("b", "a");
    }

    [Fact]
    public void AddGeoAssetsModules_RunsModuleServiceRegistration()
    {
        var builder = NewBuilder("a");

        builder.AddGeoAssetsModules(new RecordingModule("a", configure: s => s.AddSingleton("marker")));
        using var app = builder.Build();

        app.Services.GetRequiredService<string>().Should().Be("marker");
    }

    [Fact]
    public async Task AddGeoAssetsModules_NoModulesSection_EnablesNone_AndHostStillBoots()
    {
        var calls = new List<string>();
        var builder = NewBuilder();
        builder.AddGeoAssetsModules(new RecordingModule("a", calls));
        await using var app = builder.Build();

        app.MapGeoAssetsModules();
        await app.StartAsync();

        app.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Should().BeEmpty();
        calls.Should().BeEmpty();
    }

    [Fact]
    public void AddGeoAssetsModules_UnknownConfiguredId_ThrowsBeforeBuild()
    {
        var builder = NewBuilder("ghost");

        var act = () => builder.AddGeoAssetsModules(new RecordingModule("a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ghost*");
    }

    [Fact]
    public void AddGeoAssetsModules_CalledTwice_Throws()
    {
        var builder = NewBuilder();
        builder.AddGeoAssetsModules();

        var act = () => builder.AddGeoAssetsModules();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    [Fact]
    public void Extensions_NullArguments_Throw()
    {
        var builder = NewBuilder();

        var nullBuilder = () => GeoAssetsModuleHostingExtensions.AddGeoAssetsModules(null!);
        var nullBuiltIns = () => builder.AddGeoAssetsModules(null!);
        var nullMap = () => GeoAssetsModuleHostingExtensions.MapGeoAssetsModules(null!);

        nullBuilder.Should().Throw<ArgumentNullException>();
        nullBuiltIns.Should().Throw<ArgumentNullException>();
        nullMap.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public Task SeedGeoAssetsModulesAsync_NullApp_Throws()
    {
        var act = async () => await GeoAssetsModuleHostingExtensions.SeedGeoAssetsModulesAsync(null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task MapGeoAssetsModules_MapsEachEnabledModulesEndpoints()
    {
        var builder = NewBuilder("a", "b");
        builder.AddGeoAssetsModules(Serving("a", "/a/ping", "pong-a"), Serving("b", "/b/ping", "pong-b"));
        await using var app = builder.Build();

        app.MapGeoAssetsModules();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var responseA = await client.GetAsync(new Uri("/a/ping", UriKind.Relative));
        using var responseB = await client.GetAsync(new Uri("/b/ping", UriKind.Relative));

        responseA.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await responseA.Content.ReadAsStringAsync()).Should().Be("pong-a");
        (await responseB.Content.ReadAsStringAsync()).Should().Be("pong-b");
    }

    [Fact]
    public async Task MapGeoAssetsModules_ModuleMappingMiddlewareBackedEndpoint_Works()
    {
        var builder = NewBuilder("health");
        builder.AddGeoAssetsModules(new RecordingModule(
            "health",
            configure: services => services.AddHealthChecks(),
            map: endpoints => endpoints.MapHealthChecks("/module-health")));
        await using var app = builder.Build();

        app.MapGeoAssetsModules();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/module-health", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapGeoAssetsModules_DisabledModulesEndpoints_AreNotMapped()
    {
        var builder = NewBuilder("a");
        builder.AddGeoAssetsModules(Serving("a", "/a/ping", "pong-a"), Serving("b", "/b/ping", "pong-b"));
        await using var app = builder.Build();

        app.MapGeoAssetsModules();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/b/ping", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapGeoAssetsModules_TwoModulesMappingSameRoute_ThrowsInvalidOperationException()
    {
        var builder = NewBuilder("a", "b");
        builder.AddGeoAssetsModules(Serving("a", "/shared/{id}", "a"), Serving("b", "/Shared/{other}", "b"));
        await using var app = builder.Build();

        var act = () => app.MapGeoAssetsModules();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'a'*'b'*");
    }

    [Fact]
    public async Task SeedGeoAssetsModulesAsync_RunsSeedersInEnabledOrder_SkippingModulesWithoutSeeder()
    {
        var calls = new List<string>();
        var builder = NewBuilder("b", "plain", "a");
        builder.AddGeoAssetsModules(
            new SeedingRecordingModule("a", calls), new SeedingRecordingModule("b", calls), new RecordingModule("plain", calls));
        await using var app = builder.Build();

        await app.SeedGeoAssetsModulesAsync(CancellationToken.None);

        calls.Where(c => c.StartsWith("seed:", StringComparison.Ordinal)).Should().Equal("seed:b", "seed:a");
    }

    [Fact]
    public async Task SeedGeoAssetsModulesAsync_ForwardsCancellationToken()
    {
        var calls = new List<string>();
        var tokens = new List<CancellationToken>();
        var builder = NewBuilder("a");
        builder.AddGeoAssetsModules(new SeedingRecordingModule("a", calls, tokens));
        await using var app = builder.Build();
        using var cts = new CancellationTokenSource();

        await app.SeedGeoAssetsModulesAsync(cts.Token);

        tokens.Should().ContainSingle().Which.Should().Be(cts.Token);
    }
}
