using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Server.Catalogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Server.Tests.Catalogs;

public sealed class CatalogPoolTests
{
    private static readonly IReadOnlyDictionary<string, string> NoSettings = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly RecordingLogger<CatalogPool> _logger = new();

    private static CatalogDefinition Definition(string id, string plugin = TestProviderPlugin.DefaultId, IReadOnlyDictionary<string, string>? settings = null) =>
        new(id, plugin, settings ?? NoSettings);

    private CatalogPool Pool(IEnumerable<CatalogDefinition> definitions, params IProviderPlugin[] plugins) =>
        new([.. definitions], new ProviderPluginRegistry(plugins), _logger);

    private async Task<(CatalogPool Pool, TestProviderPlugin Plugin)> InitializedAsync(params string[] ids)
    {
        var plugin = new TestProviderPlugin();
        var pool = Pool(ids.Select(id => Definition(id)), plugin);
        await pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());
        return (pool, plugin);
    }

    [Fact]
    public async Task InitializeAsync_BuildsEveryCatalogInConfigurationOrder()
    {
        var (pool, plugin) = await InitializedAsync("primary", "secondary", "archive");

        pool.CatalogIds.Should().Equal("primary", "secondary", "archive");
        plugin.Created.Should().HaveCount(3);
        pool.GetCatalog("archive").UnwrapToConcrete().Should().BeSameAs(plugin.Created[2]);
    }

    [Fact]
    public async Task DefaultCatalog_IsThePrimaryCatalog()
    {
        var (pool, plugin) = await InitializedAsync("secondary", "primary");

        pool.DefaultCatalogId.Should().Be("primary");
        pool.DefaultCatalog.Should().BeSameAs(pool.GetCatalog("primary"));
        pool.DefaultCatalog.UnwrapToConcrete().Should().BeSameAs(plugin.Created[1]);
    }

    [Fact]
    public async Task GetCatalog_IgnoresCase()
    {
        var (pool, _) = await InitializedAsync("primary", "Secondary");

        pool.GetCatalog("SECONDARY").Should().BeSameAs(pool.GetCatalog("secondary"));
    }

    [Fact]
    public async Task GetCatalog_UnknownId_Throws()
    {
        var (pool, _) = await InitializedAsync("primary");

        var act = () => pool.GetCatalog("nope");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*'nope'*");
    }

    [Fact]
    public async Task TryGetCatalog_UnknownId_ReturnsFalse_AndKnownIdReturnsTheCatalog()
    {
        var (pool, _) = await InitializedAsync("primary");

        pool.TryGetCatalog("nope", out var missing).Should().BeFalse();
        missing.Should().BeNull();
        pool.TryGetCatalog("PRIMARY", out var found).Should().BeTrue();
        found.Should().BeSameAs(pool.DefaultCatalog);
    }

    [Fact]
    public async Task TryGetCatalog_NullId_Throws()
    {
        var (pool, _) = await InitializedAsync("primary");

        var act = () => pool.TryGetCatalog(null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Catalogs_AreWrappedInTheValidatingDecorator()
    {
        var (pool, plugin) = await InitializedAsync("primary");

        pool.DefaultCatalog.Should().BeOfType<ValidatingAssetProvider>();
        pool.DefaultCatalog.UnwrapToConcrete().Should().BeSameAs(plugin.Created[0]);
    }

    [Fact]
    public async Task Catalogs_RejectAnAssetTypeWithInvalidRoles()
    {
        var (pool, _) = await InitializedAsync("primary");

        var act = () => pool.DefaultCatalog.AddAssetType(new AssetType { Name = "Bad", Roles = ["Not Valid"] });

        act.Should().Throw<InvalidAssetTypeRolesException>();
    }

    [Fact]
    public async Task GetCapability_FoundThroughTheValidatingWrapper()
    {
        var (pool, plugin) = await InitializedAsync("primary");

        pool.GetCapability<ITestCatalogCapability>("primary").Should().BeSameAs(plugin.Created[0]);
    }

    [Fact]
    public async Task GetCapability_ThePoolCatalogItselfWhenItImplementsTheCapability()
    {
        var (pool, _) = await InitializedAsync("primary");

        pool.GetCapability<IAsyncDisposable>("primary").Should().BeSameAs(pool.DefaultCatalog);
    }

    [Fact]
    public async Task GetCapability_NotImplemented_ReturnsNull()
    {
        var (pool, _) = await InitializedAsync("primary");

        pool.GetCapability<IFormattable>("primary").Should().BeNull();
    }

    [Fact]
    public async Task GetCapability_UnknownId_Throws()
    {
        var (pool, _) = await InitializedAsync("primary");

        var act = () => pool.GetCapability<ITestCatalogCapability>("nope");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task InitializeAsync_PassesTheSettingsToThePlugin_CaseInsensitively()
    {
        var plugin = new TestProviderPlugin();
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["connectionStringName"] = "Other" };
        var pool = Pool([Definition("primary", settings: settings)], plugin);

        await pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        plugin.Configs.Should().ContainSingle();
        plugin.Configs[0].Get("CONNECTIONSTRINGNAME").Should().Be("Other");
        plugin.Configs[0].Has("connectionStringName").Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_PluginIdIsMatchedIgnoringCase()
    {
        var plugin = new TestProviderPlugin();
        var pool = Pool([Definition("primary", plugin: "TEST-MEMORY")], plugin);

        await pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        pool.CatalogIds.Should().Equal("primary");
    }

    [Fact]
    public async Task InitializeAsync_UnregisteredPlugin_SkipsTheCatalogAndLogsAWarning()
    {
        var plugin = new TestProviderPlugin();
        var pool = Pool([Definition("primary"), Definition("ghost", plugin: "not-registered")], plugin);

        await pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        pool.CatalogIds.Should().Equal("primary");
        pool.TryGetCatalog("ghost", out _).Should().BeFalse();
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.EventId == 3710)
            .Which.Message.Should().Contain("ghost").And.Contain("not-registered");
    }

    [Fact]
    public async Task InitializeAsync_LogsEachBuiltCatalogAsInformation()
    {
        await InitializedAsync("primary");

        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information && e.EventId == 3711)
            .Which.Message.Should().Contain("primary");
    }

    [Fact]
    public async Task InitializeAsync_AllPluginsUnregistered_LeavesAnEmptyPoolThatStillAnswers()
    {
        var pool = Pool([Definition("primary", plugin: "postgres")]);

        await pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        pool.CatalogIds.Should().BeEmpty();
        pool.TryGetCatalog("primary", out _).Should().BeFalse();
        var act = () => pool.DefaultCatalog;
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task InitializeAsync_PluginThrows_PropagatesAndDisposesTheCatalogsAlreadyBuilt()
    {
        var good = new TestProviderPlugin();
        var pool = Pool([Definition("primary"), Definition("broken", plugin: "throwing")], good, new ThrowingProviderPlugin("throwing"));

        var act = () => pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unreachable*");
        good.Created.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Accessors_BeforeInitialize_Throw()
    {
        var pool = Pool([Definition("primary")], new TestProviderPlugin());

        pool.CatalogIds.Should().BeEmpty();
        var defaultCatalog = () => pool.DefaultCatalog;
        var get = () => pool.GetCatalog("primary");
        var tryGet = () => pool.TryGetCatalog("primary", out _);
        var capability = () => pool.GetCapability<ITestCatalogCapability>("primary");
        defaultCatalog.Should().Throw<InvalidOperationException>().WithMessage("*not initialized*");
        get.Should().Throw<InvalidOperationException>().WithMessage("*not initialized*");
        tryGet.Should().Throw<InvalidOperationException>().WithMessage("*not initialized*");
        capability.Should().Throw<InvalidOperationException>().WithMessage("*not initialized*");
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_Throws()
    {
        var (pool, plugin) = await InitializedAsync("primary");

        var act = () => pool.InitializeAsync(new ServiceCollection().BuildServiceProvider());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already initialized*");
        plugin.Created.Should().ContainSingle();
    }

    [Fact]
    public Task InitializeAsync_NullServices_Throws()
    {
        var pool = Pool([Definition("primary")], new TestProviderPlugin());

        var act = () => pool.InitializeAsync(null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DisposesEachProviderOnce()
    {
        var (pool, plugin) = await InitializedAsync("primary", "secondary");

        await pool.DisposeAsync();
        await pool.DisposeAsync();

        plugin.Created.Should().OnlyContain(p => p.DisposeCount == 1);
    }

    [Fact]
    public Task DisposeAsync_BeforeInitialize_DoesNotThrow()
    {
        var pool = Pool([Definition("primary")], new TestProviderPlugin());

        var act = () => pool.DisposeAsync().AsTask();

        return act.Should().NotThrowAsync();
    }
}
