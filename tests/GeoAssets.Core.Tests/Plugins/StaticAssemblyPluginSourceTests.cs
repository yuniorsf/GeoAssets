using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Core.Tests.Plugins.FakePluginModules;

namespace GeoAssets.Core.Tests.Plugins;

/// <summary>
/// XD01-426: <see cref="StaticAssemblyPluginSource"/>, <see cref="ProviderPluginModuleScanner"/> and
/// <see cref="ProviderPluginModuleDescriptor"/>. Scanning public modules is covered over the real provider modules in
/// <c>GeoAssets.Provider.PluginModules.Tests</c> — this assembly has none.
/// </summary>
public sealed class StaticAssemblyPluginSourceTests
{
    [Fact]
    public void IsAPluginSourceNamedStatic_StartingEmpty()
    {
        var source = new StaticAssemblyPluginSource();

        source.Should().BeAssignableTo<IPluginAssemblySource>();
        source.Name.Should().Be("static");
        source.GetModuleTypes().Should().BeEmpty();
    }

    [Fact]
    public void OffersTheAddedModuleTypes_InTheOrderAdded()
    {
        var alpha = typeof(AlphaModule);

        var source = new StaticAssemblyPluginSource()
            .Add<CharlieModule>()
            .Add(alpha)
            .Add<BetaServerModule>();

        source.GetModuleTypes().Select(d => d.ModuleType)
            .Should().Equal(typeof(CharlieModule), typeof(AlphaModule), typeof(BetaServerModule));
    }

    [Fact]
    public void GetModuleTypes_IsASnapshot()
    {
        var source = new StaticAssemblyPluginSource().Add<AlphaModule>();

        var before = source.GetModuleTypes();
        source.Add<BetaServerModule>();

        before.Should().ContainSingle();
        source.GetModuleTypes().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(IProviderPluginModule))]
    [InlineData(typeof(FakeModuleBase))]
    [InlineData(typeof(GenericModule<>))]
    public void Add_RejectsATypeThatIsNotAConcreteModuleClass(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var act = () => new StaticAssemblyPluginSource().Add(type);

        act.Should().Throw<ArgumentException>().WithParameterName("moduleType")
            .WithMessage($"{type.FullName} (*) is not a provider plugin module*");
    }

    [Fact]
    public void Add_RejectsNull()
    {
        var act = () => new StaticAssemblyPluginSource().Add(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("moduleType");
    }

    [Fact]
    public void Add_AcceptsAClosedGenericOrNonPublicModule_ItWasNamedExplicitly()
    {
        var source = new StaticAssemblyPluginSource().Add<GenericModule<int>>().Add<InternalModule>();

        source.GetModuleTypes().Select(d => d.ModuleType).Should().Equal(typeof(GenericModule<int>), typeof(InternalModule));
    }

    [Fact]
    public void Scan_SkipsNonPublicTypes_AndTypesThatAreNotConcreteModuleClasses()
    {
        var assembly = new FakeAssembly(
        [
            typeof(string),
            typeof(IProviderPluginModule),
            typeof(FakeModuleBase),
            typeof(GenericModule<>),
            typeof(AlphaModule),
            typeof(InternalModule),
            typeof(ModuleStruct),
        ]);

        ProviderPluginModuleScanner.Scan(assembly).Should().BeEmpty();
    }

    [Fact]
    public void Scan_OfAnAssemblyWithoutPublicModules_FindsNone()
    {
        ProviderPluginModuleScanner.Scan(typeof(AlphaModule).Assembly).Should().BeEmpty();
    }

    [Fact]
    public void Scan_AnAssemblyWithTypesThatCannotLoad_FailsFast_WithTheLoaderErrors()
    {
        var failure = new ReflectionTypeLoadException(
            [typeof(AlphaModule), null],
            [new FileNotFoundException("Could not load 'Missing.Dependency'."), null, new FileNotFoundException("Could not load 'Missing.Dependency'.")]);

        var act = () => ProviderPluginModuleScanner.Scan(new FakeAssembly([], failure));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot load the types of provider plugin assembly 'Fake.Plugins, Version=1.0.0.0*': Could not load 'Missing.Dependency'. Its provider plugin modules can't be discovered*")
            .WithInnerException<ReflectionTypeLoadException>();
    }

    [Fact]
    public void Scan_ATypeLoadFailureWithoutLoaderMessages_ReportsTheExceptionItself()
    {
        var failure = new ReflectionTypeLoadException([null], [null], "Types failed to load.");

        var act = () => ProviderPluginModuleScanner.Scan(new FakeAssembly([], failure));

        act.Should().Throw<InvalidOperationException>().WithMessage("*': Types failed to load. Its provider plugin modules*");
    }

    [Fact]
    public void Scan_RejectsNull()
    {
        var act = () => ProviderPluginModuleScanner.Scan(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact]
    public void AddAssembly_AddsWhatTheScanFinds_AfterTheModulesAlreadyAdded()
    {
        var source = new StaticAssemblyPluginSource().Add<BetaServerModule>().AddAssembly(new FakeAssembly([typeof(AlphaModule)]));

        source.GetModuleTypes().Select(d => d.ModuleType).Should().Equal(typeof(BetaServerModule));
    }

    [Fact]
    public void AddAssembly_PropagatesTheScanFailure()
    {
        var failure = new ReflectionTypeLoadException([null], [new TypeLoadException("bad type")]);

        var act = () => new StaticAssemblyPluginSource().AddAssembly(new FakeAssembly([], failure));

        act.Should().Throw<InvalidOperationException>().WithMessage("Cannot load the types of provider plugin assembly*bad type*");
    }

    [Theory]
    [InlineData(typeof(AlphaModule), true)]
    [InlineData(typeof(InternalModule), true)]
    [InlineData(typeof(GenericModule<int>), true)]
    [InlineData(typeof(GenericModule<>), false)]
    [InlineData(typeof(FakeModuleBase), false)]
    [InlineData(typeof(IProviderPluginModule), false)]
    [InlineData(typeof(FakeModulePlugin), false)]
    [InlineData(typeof(ModuleStruct), false)]
    public void IsModuleType_AcceptsOnlyConcreteNonGenericModuleClasses(Type type, bool expected)
    {
        ProviderPluginModuleDescriptor.IsModuleType(type).Should().Be(expected);
    }

    [Fact]
    public void IsModuleType_RejectsNull()
    {
        var act = () => ProviderPluginModuleDescriptor.IsModuleType(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("type");
    }

    [Fact]
    public void Descriptor_KeepsItsModuleType()
    {
        new ProviderPluginModuleDescriptor(typeof(AlphaModule)).ModuleType.Should().Be<AlphaModule>();
    }

    private readonly struct ModuleStruct : IProviderPluginModule
    {
        public string PluginId => "struct";
        public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
        }
    }
}
