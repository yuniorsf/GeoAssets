using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.MAUI.Extensions;
using GeoAssets.MAUI.Services.Map;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.PMTiles.Tests;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.MapTiles;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-460 — MAUI's map composition as <c>MauiProgram.AddAssetsAndMap</c> makes it: with the default render mode
/// (Leaflet) <see cref="MauiMapLibreServiceCollectionExtensions.AddMauiMapLibreMode"/> adds nothing but the unchanged host
/// page, so the options, the interops and the renderers stay as before; with <c>MapInterop:RenderMode = MapLibre</c> the
/// kernel's tiles and an offline vector pack's tiles are drawn (their interops registered), the libraries come from the
/// bundle, a long press opens the context menu, and the host page's CSP is widened for the tile origin and the worker.
/// </summary>
public sealed class MauiMapLibreModeTests : IDisposable
{
    private const string Gateway = "https://gw.example.com:8443";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-maplibre-" + Guid.NewGuid().ToString("N"));

    public MauiMapLibreModeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GeoAssetsServer:BaseUrl"] = Gateway,
                ["MapInterop:SinglePass"] = "false",
                ["MapInterop:BatchSize"] = "5",
            }.Concat(values.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value))))
            .Build();

    /// <summary>MAUI's map registrations (MauiProgram.AddAssetsAndMap and AddCrossCuttingServices), with test doubles for the JS runtime and the provider.</summary>
    private static ServiceProvider Compose(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IJSRuntime, NoJsRuntime>();
        services.AddSingleton<IProviderPool, ProviderPool>();
        services.AddSingleton<IAssetProvider>(sp => new ActiveAssetProvider(sp.GetRequiredService<IProviderPool>()));
        services.AddSingleton<FeatureRenderPipeline>();
        services.AddSingleton(new ApiScopeRoutes(new Uri(Gateway), null));
        services.AddProviderMapLayerRenderers();
        services.Configure<MapInteropOptions>(configuration.GetSection("MapInterop"));
        services.AddScoped<MapInteropService>();
        services.AddScoped(sp => new ObservableMapInterop(
            sp.GetRequiredService<MapInteropService>(),
            sp.GetRequiredService<MapInteropService>(),
            sp.GetRequiredService<IOptions<MapInteropOptions>>().Value.RenderMode,
            sp.GetRequiredService<ILogger<ObservableMapInterop>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IMapInterop>(sp => sp.GetRequiredService<ObservableMapInterop>());
        services.AddScoped<IMapSelectionInterop>(sp => sp.GetRequiredService<ObservableMapInterop>());
        services.AddScoped<IMapLocalTileLayerInterop>(sp => sp.GetRequiredService<MapInteropService>());
        services.AddMauiMapLibreMode(configuration);
        return services.BuildServiceProvider();
    }

    private async Task<PMTilesPackAssetProvider> VectorPackAsync()
    {
        var pack = await PMTilesPack.OpenAsync(TestPacks.Write(_directory, [new TileAddress(0, 0, 0)], maxZoom: 0), CancellationToken.None);
        return new PMTilesPackAssetProvider(pack);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Leaflet")]
    [InlineData("Canvas")]
    public async Task TheDefaultComposition_IsUnchanged(string? renderMode)
    {
        var configuration = renderMode is null ? Configuration() : Configuration(("MapInterop:RenderMode", renderMode));
        var services = new ServiceCollection();

        services.AddMauiMapLibreMode(configuration);

        var only = services.Should().ContainSingle("nothing but the host page is added in a Leaflet mode").Subject;
        only.ServiceType.Should().Be<MauiHostPage>();
        only.ImplementationInstance.Should().BeSameAs(MauiHostPage.Unchanged);
        MauiHostPage.Unchanged.Transform.Should().BeNull("the bundled index.html is served byte for byte");

        await using var provider = Compose(configuration);
        await using var scope = provider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MapInteropOptions>>().Value;
        options.MapLibre.LibraryBaseUrl.Should().BeNull();
        options.MapLibre.LongPressContextMenu.Should().BeFalse();
        scope.ServiceProvider.GetService<IMapTileLayerInterop>().Should().BeNull("the kernel's entry keeps being drawn as features");
        scope.ServiceProvider.GetService<IMapLocalVectorTileLayerInterop>().Should().BeNull();
        scope.ServiceProvider.GetRequiredService<MapInteropService>().MapLibreMap.Should().BeNull();

        using var pack = await VectorPackAsync();
        scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>().Resolve(pack)
            .Should().BeOfType<VectorFeatureMapLayerRenderer>("a vector pack is still drawn as Leaflet GeoJSON");
    }

    [Fact]
    public async Task MapLibre_RegistersTheTileInterops_TheBundledLibraries_AndTheLongPress()
    {
        await using var provider = Compose(Configuration(("MapInterop:RenderMode", "MapLibre")));
        await using var scope = provider.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<MapInteropService>();
        scope.ServiceProvider.GetRequiredService<IMapTileLayerInterop>().Should().BeSameAs(service);
        scope.ServiceProvider.GetRequiredService<IMapLocalVectorTileLayerInterop>().Should().BeSameAs(service);
        scope.ServiceProvider.GetRequiredService<IMapLocalTileLayerInterop>().Should().BeSameAs(service);
        service.MapLibreMap.Should().Be(new MapLibreMapOptions(
            Gateway + "/tiles/style.json", Gateway + "/", MauiMapLibreServiceCollectionExtensions.LibraryBaseUrl, LongPressContextMenu: true));

        var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();
        using var pack = await VectorPackAsync();
        chain.Resolve(pack).Should().BeOfType<LocalVectorTileMapLayerRenderer>("the pack is drawn from its tiles");
        chain.Resolve(new ValidatingAssetProvider(pack)).Should().BeOfType<LocalVectorTileMapLayerRenderer>();
    }

    [Fact]
    public async Task MapLibre_KeepsWhatIsConfigured()
    {
        await using var provider = Compose(Configuration(
            ("MapInterop:RenderMode", "maplibre"),
            ("MapInterop:MapLibre:LibraryBaseUrl", "https://cdn.example/libs/"),
            ("MapInterop:MapLibre:LongPressContextMenu", "false")));
        await using var scope = provider.CreateAsyncScope();

        var map = scope.ServiceProvider.GetRequiredService<MapInteropService>().MapLibreMap!;
        map.LibraryBaseUrl.Should().Be("https://cdn.example/libs/");
        map.LongPressContextMenu.Should().BeFalse();
    }

    [Fact]
    public void MapLibre_WidensTheBundledHostPage_ForTheTileStylesOrigin()
    {
        var services = new ServiceCollection();
        services.AddMauiMapLibreMode(Configuration(
            ("MapInterop:RenderMode", "MapLibre"), ("MapInterop:MapLibre:StyleUrl", "https://tiles.example.org/kernel/tiles/style.json")));
        var hostPage = services.Single(d => d.ServiceType == typeof(MauiHostPage)).ImplementationInstance.Should().BeOfType<MauiHostPage>().Subject;

        var page = hostPage.Transform!(File.ReadAllText(BundledHostPage));

        DirectivesOf(page)["connect-src"].Should().Equal("'self'", "https://*.tile.openstreetmap.org", "https://tiles.example.org");
    }

    [Fact]
    public void MapLibre_WithoutAGatewayOrAnAbsoluteStyle_FailsTheStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["MapInterop:RenderMode"] = "MapLibre" })
            .Build();

        FluentActions.Invoking(() => new ServiceCollection().AddMauiMapLibreMode(configuration))
            .Should().Throw<InvalidOperationException>().WithMessage("*GeoAssetsServer:BaseUrl*");
        FluentActions.Invoking(() => MauiMapLibreServiceCollectionExtensions.AddMauiMapLibreMode(null!, configuration)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection().AddMauiMapLibreMode(null!)).Should().Throw<ArgumentNullException>();
    }

    // ── The host page's Content-Security-Policy ───────────────────────────────

    private static string BundledHostPage => Path.Combine(AppContext.BaseDirectory, "HostPage", "index.html");

    private static Dictionary<string, List<string>> DirectivesOf(string page)
    {
        var start = page.IndexOf("content=\"", page.IndexOf("Content-Security-Policy", StringComparison.Ordinal), StringComparison.Ordinal) + "content=\"".Length;
        var policy = page[start..page.IndexOf('"', start)];
        return policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(d => d[0], d => d.Skip(1).ToList(), StringComparer.Ordinal);
    }

    [Fact]
    public void TheWidenedPolicy_AddsTheGatewayToConnectSrc_AndBlobWorkers_AndKeepsEveryOtherDirective()
    {
        var original = File.ReadAllText(BundledHostPage);
        var before = DirectivesOf(original);

        var widened = MapLibreHostPage.WidenContentSecurityPolicy(original, new Uri("http://localhost:5070/api/"));
        var after = DirectivesOf(widened);

        after["connect-src"].Should().Equal([.. before["connect-src"], "http://localhost:5070"]);
        after["worker-src"].Should().Equal("blob:");
        after.Keys.Except(["worker-src"], StringComparer.Ordinal).Should().BeEquivalentTo(before.Keys);
        foreach (var directive in before.Keys.Except(["connect-src"], StringComparer.Ordinal))
            after[directive].Should().Equal(before[directive], directive + " is untouched: no script, style or image origin is added");
        widened.Replace(widened[widened.IndexOf("<meta http-equiv", StringComparison.Ordinal)..(widened.IndexOf("/>", widened.IndexOf("Content-Security-Policy", StringComparison.Ordinal), StringComparison.Ordinal))], string.Empty, StringComparison.Ordinal)
            .Should().Be(original.Replace(original[original.IndexOf("<meta http-equiv", StringComparison.Ordinal)..(original.IndexOf("/>", original.IndexOf("Content-Security-Policy", StringComparison.Ordinal), StringComparison.Ordinal))], string.Empty, StringComparison.Ordinal),
                "nothing but the policy changes");
    }

    [Fact]
    public void WideningTwice_IsWideningOnce_AndAnExistingWorkerSrcGainsBlob()
    {
        var original = File.ReadAllText(BundledHostPage);
        var gateway = new Uri("https://gw.example.com");

        var once = MapLibreHostPage.WidenContentSecurityPolicy(original, gateway);
        MapLibreHostPage.WidenContentSecurityPolicy(once, gateway).Should().Be(once);

        var withWorkers = """<meta http-equiv="Content-Security-Policy" content="default-src 'self'; worker-src 'self'; connect-src 'self'" />""";
        DirectivesOf(MapLibreHostPage.WidenContentSecurityPolicy(withWorkers, gateway))["worker-src"].Should().Equal("'self'", "blob:");
    }

    [Fact]
    public void APageWithoutAPolicyOrConnectSrc_OrANonHttpOrigin_IsRefused()
    {
        var gateway = new Uri("https://gw.example.com");

        FluentActions.Invoking(() => MapLibreHostPage.WidenContentSecurityPolicy("<html></html>", gateway)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => MapLibreHostPage.WidenContentSecurityPolicy("""<meta http-equiv="Content-Security-Policy" content="default-src 'self'" />""", gateway))
            .Should().Throw<InvalidOperationException>().WithMessage("*connect-src*");
        FluentActions.Invoking(() => MapLibreHostPage.WidenContentSecurityPolicy(null!, gateway)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => MapLibreHostPage.OriginOf(new Uri("file:///tmp/style.json"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MapLibreHostPage.OriginOf(new Uri("tiles/style.json", UriKind.Relative))).Should().Throw<ArgumentException>();
        MapLibreHostPage.OriginOf(new Uri("https://gw.example.com:8443/a/b?c")).Should().Be("https://gw.example.com:8443");
    }

    // ── The host page file provider ───────────────────────────────────────────

    [Fact]
    public void TheFileProvider_RewritesTheHostPageOnce_AndServesEveryOtherFileAsItIs()
    {
        var inner = new MemoryFiles(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["index.html"] = "<p>original</p>",
            ["lib/maplibre/x.js"] = "js",
        });
        var calls = 0;
        var files = new HostPageFileProvider(inner, "index.html", html => { calls++; return html.Replace("original", "rewritten", StringComparison.Ordinal); });

        ReadAll(files.GetFileInfo("index.html")).Should().Be("<p>rewritten</p>");
        ReadAll(files.GetFileInfo("/index.html")).Should().Be("<p>rewritten</p>");
        files.GetFileInfo("index.html").Length.Should().Be("<p>rewritten</p>".Length);
        files.GetFileInfo("index.html").Name.Should().Be("index.html");
        files.GetFileInfo("index.html").PhysicalPath.Should().BeNull();
        files.GetFileInfo("index.html").IsDirectory.Should().BeFalse();
        calls.Should().Be(1);
        ReadAll(files.GetFileInfo("lib/maplibre/x.js")).Should().Be("js");
        files.GetFileInfo("missing.js").Exists.Should().BeFalse();
        files.GetDirectoryContents("lib").Should().BeSameAs(inner.Directory);
        files.Watch("*").Should().BeSameAs(Microsoft.Extensions.FileProviders.NullChangeToken.Singleton);
    }

    [Fact]
    public void TheFileProvider_WithoutAHostPage_PassesTheMissingFileOn()
    {
        var files = new HostPageFileProvider(new MemoryFiles(new Dictionary<string, string>(StringComparer.Ordinal)), "index.html", html => html + "!");

        files.GetFileInfo("index.html").Exists.Should().BeFalse();
    }

    private static string ReadAll(Microsoft.Extensions.FileProviders.IFileInfo file)
    {
        using var reader = new StreamReader(file.CreateReadStream());
        return reader.ReadToEnd();
    }

    private sealed class MemoryFiles(Dictionary<string, string> files) : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public Microsoft.Extensions.FileProviders.IDirectoryContents Directory { get; } = Microsoft.Extensions.FileProviders.NotFoundDirectoryContents.Singleton;

        public Microsoft.Extensions.FileProviders.IFileInfo GetFileInfo(string subpath) =>
            files.TryGetValue(subpath.TrimStart('/'), out var text)
                ? new MemoryFile(Path.GetFileName(subpath), text)
                : new Microsoft.Extensions.FileProviders.NotFoundFileInfo(subpath);

        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string subpath) => Directory;

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => Microsoft.Extensions.FileProviders.NullChangeToken.Singleton;
    }

    private sealed class MemoryFile(string name, string text) : Microsoft.Extensions.FileProviders.IFileInfo
    {
        public bool Exists => true;
        public long Length => System.Text.Encoding.UTF8.GetByteCount(text);
        public string? PhysicalPath => "/memory/" + name;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }

    // ── An offline vector pack in MapLibre mode, end to end on the .NET side ──

    [Fact]
    public async Task AVectorPack_ReachesMapLibre_AsItsReader_AndItsTilesComeFromThePack()
    {
        var js = new RecordingJsRuntime();
        await using var service = new MapInteropService(js, new ActiveAssetProvider(new ProviderPool()),
            Options.Create(new MapInteropOptions { RenderMode = MapRenderMode.MapLibre }), routes: new ApiScopeRoutes(new Uri(Gateway), null),
            logger: NullLogger<MapInteropService>.Instance);
        using var pack = await VectorPackAsync();
        var entry = new ProviderEntry { Name = "Pack", Provider = pack };
        var renderer = new LocalVectorTileMapLayerRenderer(service, service);

        await renderer.RenderAsync(MapLayerRenderRequest.For("map", entry));

        var call = js.Calls.Single(c => string.Equals(c.Identifier, "GeoAssetsMapLibre.addLocalVectorLayer", StringComparison.Ordinal));
        call.Args[1].Should().Be(entry.Id.ToString());
        var reader = call.Args[2].Should().BeOfType<DotNetObjectReference<LocalVectorTileReader>>().Subject.Value;
        var tile = await reader.GetTileFromJsAsync(0, 0, 0);
        tile.Should().NotBeNull();
        tile![0].Should().Be(0x1A, "an uncompressed MVT starts with its first layer (field 3, length-delimited), not a gzip header");
        (await reader.GetTileFromJsAsync(1, 0, 0)).Should().BeNull("past the pack's highest zoom MapLibre overzooms; nothing is read");
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
