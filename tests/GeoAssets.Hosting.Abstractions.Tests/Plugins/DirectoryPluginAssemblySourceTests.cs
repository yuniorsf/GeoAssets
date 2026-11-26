using System.Runtime.Loader;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Services;
using GeoAssets.Hosting.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>XD01-427 (ADR-002 D2/D7): <see cref="DirectoryPluginAssemblySource"/> over the real fixture plugins.</summary>
public sealed class DirectoryPluginAssemblySourceTests : IDisposable
{
    internal const string FlatPattern = "GeoAssets.Provider.*.dll";

    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private readonly TempPluginDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void EachPluginFolder_LoadsIntoItsOwnContext_AndOffersItsModules()
    {
        var source = new DirectoryPluginAssemblySource(PluginFixtures.Root);

        var modules = source.GetModuleTypes();

        modules.Select(m => m.ModuleType.FullName).Should().Equal(
            "GeoAssets.PluginFixtures.Alpha.AlphaFixturePluginModule",
            "GeoAssets.PluginFixtures.Beta.BetaFixturePluginModule");
        source.Plugins.Select(p => p.Name).Should().Equal(PluginFixtures.Alpha, PluginFixtures.Beta);
        foreach (var plugin in source.Plugins)
        {
            AssemblyLoadContext.GetLoadContext(plugin.Assembly).Should().BeSameAs(plugin.LoadContext);
            plugin.LoadContext.PluginMainAssemblyPath.Should().Be(PluginFixtures.EntryAssembly(plugin.Name));
            plugin.Modules.Should().ContainSingle().Which.ModuleType.Assembly.Should().BeSameAs(plugin.Assembly);
        }

        source.Plugins[0].LoadContext.Should().NotBeSameAs(source.Plugins[1].LoadContext);
    }

    [Fact]
    public void ThePluginsLoadOnce_LaterCallsReturnTheSameModules()
    {
        var source = new DirectoryPluginAssemblySource(PluginFixtures.Root);
        source.Plugins.Should().BeEmpty();

        var first = source.GetModuleTypes();
        var contexts = source.Plugins.Select(p => p.LoadContext).ToList();
        var second = source.GetModuleTypes();

        second.Should().BeSameAs(first);
        source.Plugins.Select(p => p.LoadContext).Should().Equal(contexts);
    }

    [Fact]
    public void Find_LooksUpALoadedPluginByName_IgnoringCase()
    {
        var source = new DirectoryPluginAssemblySource(PluginFixtures.Root);
        source.GetModuleTypes();

        source.Find(PluginFixtures.Beta.ToUpperInvariant())!.Name.Should().Be(PluginFixtures.Beta);
        source.Find("missing").Should().BeNull();
        source.Should().BeAssignableTo<IPluginLoadContextRegistry>().Which.Plugins.Should().HaveCount(2);
    }

    [Fact]
    public void TheModules_RegisterThroughDiscovery_AlongsideAStaticSource_AsTheHostsOwnTypes()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(
            Configuration,
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<BundledModule>(),
            new DirectoryPluginAssemblySource(PluginFixtures.Root));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderPluginRegistry>();
        registry.All.Select(p => p.Id).Should().BeEquivalentTo("bundled", "fixture-alpha", "fixture-beta");
        registry.Find("fixture-alpha")!.DisplayName.Should().Be("Fixture Alpha (v1)");
        registry.Find("fixture-beta")!.DisplayName.Should().Be("Fixture Beta (v2)");
        AssemblyLoadContext.GetLoadContext(registry.Find("fixture-alpha")!.GetType().Assembly)
            .Should().BeOfType<PluginLoadContext>();
    }

    [Fact]
    public void AModuleForAnotherHost_IsSkipped()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Browser, new DirectoryPluginAssemblySource(PluginFixtures.Root));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("fixture-beta");
    }

    [Fact]
    public void APluginIdTheStaticSourceAlsoOffers_FailsDiscovery()
    {
        var services = new ServiceCollection();

        var act = () => services.AddDiscoveredProviderPlugins(
            Configuration,
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<AlphaImpostorModule>(),
            new DirectoryPluginAssemblySource(PluginFixtures.Root));

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate provider plugin module id 'fixture-alpha'*");
    }

    [Fact]
    public void AFlatGeoAssetsProviderAssembly_InTheRoot_IsAPlugin()
    {
        var entry = _temp.CopyPluginFlat(PluginFixtures.Beta, "GeoAssets.Provider.FixtureBeta.dll");
        var source = new DirectoryPluginAssemblySource(_temp.Root, FlatPattern);

        var modules = source.GetModuleTypes();

        modules.Select(m => m.ModuleType.Name).Should().Equal("BetaFixturePluginModule");
        source.Plugins.Should().ContainSingle().Which.Name.Should().Be("GeoAssets.Provider.FixtureBeta");
        source.FlatPluginFilePattern.Should().Be(FlatPattern);
        source.Plugins[0].LoadContext.PluginMainAssemblyPath.Should().Be(entry);
        PluginFixtures.InvokeModuleStatic(source.Plugins[0].LoadContext, PluginFixtures.Beta, "PrivateDependencyEdition")
            .Should().Be("v2");
    }

    [Fact]
    public void FolderPlugins_LoadBeforeFlatOnes()
    {
        _temp.CopyPluginFlat(PluginFixtures.Beta, "GeoAssets.Provider.FixtureBeta.dll");
        _temp.CopyPlugin(PluginFixtures.Alpha);

        var source = new DirectoryPluginAssemblySource(_temp.Root, FlatPattern);
        source.GetModuleTypes();

        source.Plugins.Select(p => p.Name).Should().Equal(PluginFixtures.Alpha, "GeoAssets.Provider.FixtureBeta");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void WithoutAFlatPattern_AssembliesInTheRoot_AreNotPlugins(string? pattern)
    {
        _temp.CopyPluginFlat(PluginFixtures.Beta, "GeoAssets.Provider.FixtureBeta.dll");

        var source = new DirectoryPluginAssemblySource(_temp.Root, pattern);

        source.FlatPluginFilePattern.Should().BeNull();
        source.GetModuleTypes().Should().BeEmpty();
    }

    [Theory]
    [InlineData("plugins/GeoAssets.Provider.*.dll")]
    [InlineData("plugins\\GeoAssets.Provider.*.dll")]
    [InlineData("GeoAssets.Provider.*")]
    public void AFlatPatternThatIsNotADllFileName_IsRejected(string pattern)
    {
        var act = () => new DirectoryPluginAssemblySource(_temp.Root, pattern);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{pattern}'*");
    }

    [Fact]
    public void AnEmptyDirectory_OffersNoModules()
    {
        new DirectoryPluginAssemblySource(_temp.Root).GetModuleTypes().Should().BeEmpty();
    }

    [Fact]
    public void TheRuntimesFolder_AndHiddenFolders_AreNotPlugins()
    {
        _temp.CreateFolder(Path.Combine("runtimes", "linux-x64", "native"));
        _temp.CreateFolder(".cache");

        new DirectoryPluginAssemblySource(_temp.Root).GetModuleTypes().Should().BeEmpty();
    }

    [Fact]
    public void AMissingDirectory_FailsFast()
    {
        var missing = Path.Combine(_temp.Root, "nope");

        var act = () => new DirectoryPluginAssemblySource(missing).GetModuleTypes();

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage($"*'{missing}' does not exist*{DirectoryPluginAssemblySource.DirectoryConfigurationKey}*");
    }

    [Fact]
    public void APluginFolderWithoutItsEntryAssembly_FailsFast()
    {
        _temp.CopyPlugin(PluginFixtures.Alpha, folderName: "Renamed");

        var act = () => new DirectoryPluginAssemblySource(_temp.Root).GetModuleTypes();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Renamed.dll'*<PluginName>/<PluginName>.dll*");
    }

    [Fact]
    public void AMalformedEntryAssembly_FailsFast()
    {
        var folder = _temp.CreateFolder("Broken");
        File.WriteAllText(Path.Combine(folder, "Broken.dll"), "not a .NET assembly");

        var act = () => new DirectoryPluginAssemblySource(_temp.Root).GetModuleTypes();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot load provider plugin 'Broken'*")
            .WithInnerException<BadImageFormatException>();
    }

    [Fact]
    public void AnEntryAssemblyWithoutAModule_FailsFast()
    {
        var folder = _temp.CreateFolder(PluginFixtures.PrivateDependency);
        File.Copy(
            Path.Combine(PluginFixtures.Folder(PluginFixtures.Alpha), PluginFixtures.PrivateDependency + ".dll"),
            Path.Combine(folder, PluginFixtures.PrivateDependency + ".dll"));

        var act = () => new DirectoryPluginAssemblySource(_temp.Root).GetModuleTypes();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Provider plugin '{PluginFixtures.PrivateDependency}'*offers no provider plugin module*");
    }

    [Fact]
    public void TwoPluginsWithOneName_FailFast()
    {
        _temp.CopyPlugin(PluginFixtures.Beta, folderName: "GeoAssets.Provider.FixtureBeta");
        File.Move(
            Path.Combine(_temp.Root, "GeoAssets.Provider.FixtureBeta", PluginFixtures.Beta + ".dll"),
            Path.Combine(_temp.Root, "GeoAssets.Provider.FixtureBeta", "GeoAssets.Provider.FixtureBeta.dll"));
        File.Delete(Path.Combine(_temp.Root, "GeoAssets.Provider.FixtureBeta", PluginFixtures.Beta + ".deps.json"));
        _temp.CopyPluginFlat(PluginFixtures.Beta, "GeoAssets.Provider.FixtureBeta.dll");

        var act = () => new DirectoryPluginAssemblySource(_temp.Root, FlatPattern).GetModuleTypes();

        act.Should().Throw<InvalidOperationException>().WithMessage("Two provider plugins are named 'GeoAssets.Provider.FixtureBeta'*");
    }

    [Fact]
    public void ARelativeRoot_IsRelativeToTheHostsBaseDirectory()
    {
        var source = new DirectoryPluginAssemblySource("plugin-fixtures");

        source.RootDirectory.Should().Be(PluginFixtures.Root);
        source.Name.Should().Be("directory:" + PluginFixtures.Root);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankRoot_IsRejected(string root)
    {
        var act = () => new DirectoryPluginAssemblySource(root);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FindingANullName_IsRejected()
    {
        var act = () => new DirectoryPluginAssemblySource(PluginFixtures.Root).Find(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    internal sealed class BundledModule : IProviderPluginModule
    {
        public string PluginId => "bundled";

        public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

        public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<IProviderPlugin>(new BundledPlugin());
    }

    internal sealed class AlphaImpostorModule : IProviderPluginModule
    {
        public string PluginId => "Fixture-Alpha";

        public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
        }
    }

    private sealed class BundledPlugin : IProviderPlugin
    {
        public string Id => "bundled";

        public string DisplayName => "Bundled";

        public string Description => "A provider the host bundles.";

        public IReadOnlyList<ProviderConfigField> ConfigFields => [];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
