using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Core.Tests.Plugins.FakePluginModules;

namespace GeoAssets.Core.Tests.Plugins;

/// <summary>XD01-426 (ADR-002 D2/D7): <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.</summary>
public sealed class ProviderPluginDiscoveryTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Key"] = "Value" })
        .Build();

    private static StaticAssemblyPluginSource AllFakes() => new StaticAssemblyPluginSource()
        .Add<AlphaModule>()
        .Add<BetaServerModule>()
        .Add<CharlieModule>()
        .Add<DeltaClientModule>()
        .Add<NoHostModule>();

    [Theory]
    [InlineData(ProviderHostTargets.Server, new[] { "alpha", "beta", "Charlie" })]
    [InlineData(ProviderHostTargets.Browser, new[] { "alpha", "Charlie", "delta" })]
    [InlineData(ProviderHostTargets.MobileDesktop, new[] { "alpha", "Charlie", "delta" })]
    public void RegistersTheModulesTheHostRuns_AndSkipsTheOthers(ProviderHostTargets host, string[] expected)
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration, host, AllFakes());

        RegisteredIds(services).Should().Equal(expected);
    }

    [Fact]
    public void AModuleSupportingNoHost_IsNeverRegistered()
    {
        foreach (var host in new[] { ProviderHostTargets.Server, ProviderHostTargets.Browser, ProviderHostTargets.MobileDesktop })
        {
            var services = new ServiceCollection();

            services.AddDiscoveredProviderPlugins(Configuration, host, new StaticAssemblyPluginSource().Add<NoHostModule>());

            RegisteredIds(services).Should().BeEmpty();
        }
    }

    [Fact]
    public void ModulesRegisterInPluginIdOrderIgnoringCase_WhateverTheSourceOrder()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(
            Configuration,
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<CharlieModule>().Add<BetaServerModule>(),
            new FakeSource("second", typeof(AlphaModule)));

        RegisteredIds(services).Should().Equal("alpha", "beta", "Charlie");
        services.Where(d => d.ServiceType == typeof(IProviderPlugin))
            .Select(d => ((IProviderPlugin)d.ImplementationInstance!).Id)
            .Should().Equal("alpha", "beta", "Charlie");
    }

    [Fact]
    public void EachModuleRegistersWithTheHostsConfiguration()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Browser, AllFakes());

        Registrations(services).Should().NotBeEmpty()
            .And.AllSatisfy(r => r.Configuration.Should().BeSameAs(Configuration));
    }

    [Fact]
    public void RegistersTheProviderPluginRegistry_OverThePluginsTheModulesRegistered()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, AllFakes());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var registry = provider.GetRequiredService<ProviderPluginRegistry>();
        registry.All.Select(p => p.Id).Should().Equal("alpha", "beta", "Charlie");
        registry.Find("CHARLIE").Should().NotBeNull();
        registry.Find("delta").Should().BeNull("delta doesn't run on a server");
    }

    [Fact]
    public void KeepsAProviderPluginRegistryTheHostAlreadyRegistered()
    {
        var services = new ServiceCollection();
        var hostRegistry = new ProviderPluginRegistry([]);
        services.AddSingleton(hostRegistry);

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, AllFakes());

        services.Where(d => d.ServiceType == typeof(ProviderPluginRegistry)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(hostRegistry);
    }

    [Fact]
    public void ReadsEachSourceOnce()
    {
        var source = new FakeSource("once", typeof(AlphaModule));

        new ServiceCollection().AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, source);

        source.Reads.Should().Be(1);
    }

    [Fact]
    public void EnabledPluginIds_RegistersOnlyThoseIdsIgnoringCase_AndIgnoresUnknownOnes()
    {
        var services = new ServiceCollection();
        var options = new ProviderPluginDiscoveryOptions
        {
            HostTarget = ProviderHostTargets.Server,
            EnabledPluginIds = { "ALPHA", "charlie", "not-offered" },
        };

        services.AddDiscoveredProviderPlugins(Configuration, options, AllFakes());

        RegisteredIds(services).Should().Equal("alpha", "Charlie");
    }

    [Fact]
    public void EnabledPluginIds_DoesNotOverrideTheHostFilter()
    {
        var services = new ServiceCollection();
        var options = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Browser, EnabledPluginIds = { "beta" } };

        services.AddDiscoveredProviderPlugins(Configuration, options, AllFakes());

        RegisteredIds(services).Should().BeEmpty("beta is server-only");
    }

    [Fact]
    public void DisabledPluginIds_SkipsThoseIdsIgnoringCase_AndWinsOverEnabledPluginIds()
    {
        var services = new ServiceCollection();
        var options = new ProviderPluginDiscoveryOptions
        {
            HostTarget = ProviderHostTargets.Server,
            EnabledPluginIds = { "alpha", "beta" },
            DisabledPluginIds = { "BETA", "unknown" },
        };

        services.AddDiscoveredProviderPlugins(Configuration, options, AllFakes());

        RegisteredIds(services).Should().Equal("alpha");
    }

    [Fact]
    public void Options_StartWithNoHostAndEmptyCaseInsensitiveLists()
    {
        var options = new ProviderPluginDiscoveryOptions();

        options.HostTarget.Should().Be(ProviderHostTargets.None);
        options.EnabledPluginIds.Should().BeEmpty();
        options.DisabledPluginIds.Should().BeEmpty();
        options.EnabledPluginIds.Add("Rest");
        options.EnabledPluginIds.Contains("REST").Should().BeTrue();
        options.DisabledPluginIds.Add("Rest");
        options.DisabledPluginIds.Contains("rest").Should().BeTrue();
    }

    [Fact]
    public void DuplicateIdsIgnoringCase_FailFast_NamingBothModules_AndRegisterNothing()
    {
        var services = new ServiceCollection();

        var act = () => services.AddDiscoveredProviderPlugins(
            Configuration,
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<AlphaModule>().Add<BetaServerModule>(),
            new FakeSource("plugins-dir", typeof(AlphaDuplicateModule)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate provider plugin module id 'ALPHA'*")
            .Which.Message.Should()
            .Contain(typeof(AlphaDuplicateModule).FullName).And.Contain(typeof(AlphaModule).FullName)
            .And.Contain(typeof(AlphaModule).Assembly.FullName).And.Contain("source 'static'").And.Contain("source 'plugins-dir'");
        services.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateIds_FailEvenWhenBothModulesWouldBeSkipped()
    {
        var options = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Server, DisabledPluginIds = { "alpha" } };

        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, options, new StaticAssemblyPluginSource().Add<AlphaModule>().Add<AlphaDuplicateModule>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate provider plugin module id*");
    }

    [Fact]
    public void AnIdAnEarlierCallRegistered_FailsFast_AndTheSecondCallRegistersNothing()
    {
        var services = new ServiceCollection();
        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<AlphaModule>());
        var registeredByFirstCall = services.Count;

        var act = () => services.AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<BetaServerModule>().Add<AlphaDuplicateModule>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate provider plugin module id 'ALPHA'*registered by an earlier AddDiscoveredProviderPlugins call*");
        services.Should().HaveCount(registeredByFirstCall);
    }

    [Fact]
    public void AnIdAnEarlierCallOnlySkipped_CanBeRegisteredLater()
    {
        var services = new ServiceCollection();
        var skipAlpha = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Server, DisabledPluginIds = { "alpha" } };
        services.AddDiscoveredProviderPlugins(Configuration, skipAlpha, new StaticAssemblyPluginSource().Add<AlphaModule>().Add<BetaServerModule>());

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<AlphaDuplicateModule>());

        RegisteredIds(services).Should().Equal("beta", "ALPHA");
    }

    [Fact]
    public void SeveralCalls_WithDistinctIds_RegisterEveryModule_AndOneRegistry()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<BetaServerModule>());
        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<AlphaModule>());
        using var provider = services.BuildServiceProvider();

        RegisteredIds(services).Should().Equal("beta", "alpha");
        services.Count(d => d.ServiceType == typeof(ProviderPluginRegistry)).Should().Be(1);
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("beta", "alpha");
    }

    [Fact]
    public void AModuleWhoseConstructorThrows_FailsFast_WithTheCause()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<ThrowingConstructorModule>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Provider plugin module {typeof(ThrowingConstructorModule).FullName}*source 'static'*cannot be created: constructor boom*")
            .WithInnerException<InvalidOperationException>().WithMessage("constructor boom");
    }

    [Fact]
    public void AModuleWithoutAParameterlessConstructor_FailsFast()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add(typeof(NoParameterlessConstructorModule)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Provider plugin module {typeof(NoParameterlessConstructorModule).FullName}*cannot be created*public parameterless constructor*")
            .WithInnerException<MissingMethodException>();
    }

    [Fact]
    public void AModuleWithABlankId_FailsFast()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<BlankIdModule>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Provider plugin module {typeof(BlankIdModule).FullName}*has a blank PluginId*");
    }

    [Fact]
    public void AModuleFailingToRegister_FailsFast_NamingTheModule()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<ThrowingRegistrationModule>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Provider plugin module 'faulty' {typeof(ThrowingRegistrationModule).FullName} (*source 'static') failed to register its services: registration boom*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ASourceThatCannotLoadAnAssemblysTypes_FailsFast_WithTheLoaderErrors()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<AlphaModule>(), new TypeLoadFailingSource());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Provider plugin source 'broken-dir' cannot load the types of an assembly: Could not load 'Missing.Dependency'.*")
            .WithInnerException<ReflectionTypeLoadException>();
    }

    [Fact]
    public void ASourceReturningNoList_FailsFast()
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(
            Configuration, ProviderHostTargets.Server, new NullListSource());

        act.Should().Throw<InvalidOperationException>().WithMessage("Provider plugin source 'null-list' returned no module list.");
    }

    [Theory]
    [InlineData(ProviderHostTargets.None)]
    [InlineData(ProviderHostTargets.All)]
    [InlineData(ProviderHostTargets.Server | ProviderHostTargets.Browser)]
    [InlineData((ProviderHostTargets)8)]
    public void AHostTargetThatIsNotExactlyOneHost_IsRejected(ProviderHostTargets host)
    {
        var act = () => new ServiceCollection().AddDiscoveredProviderPlugins(Configuration, host, AllFakes());

        act.Should().Throw<ArgumentException>().WithMessage("ProviderPluginDiscoveryOptions.HostTarget must name exactly one host*");
    }

    [Fact]
    public void NoSources_OrANullSource_AreRejected()
    {
        var services = new ServiceCollection();

        services.Invoking(s => s.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server))
            .Should().Throw<ArgumentException>().WithParameterName("sources");
        services.Invoking(s => s.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, AllFakes(), null!))
            .Should().Throw<ArgumentException>().WithParameterName("sources");
        services.Should().BeEmpty();
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var services = new ServiceCollection();
        var options = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Server };

        var nullServices = () => ((IServiceCollection)null!).AddDiscoveredProviderPlugins(Configuration, options, AllFakes());
        nullServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        services.Invoking(s => s.AddDiscoveredProviderPlugins(null!, options, AllFakes()))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        services.Invoking(s => s.AddDiscoveredProviderPlugins(Configuration, (ProviderPluginDiscoveryOptions)null!, AllFakes()))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        services.Invoking(s => s.AddDiscoveredProviderPlugins(Configuration, options, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public void ReturnsTheServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, AllFakes()).Should().BeSameAs(services);
    }

    private static List<FakeModuleRegistration> Registrations(IServiceCollection services) =>
        [.. services.Select(d => d.ImplementationInstance).OfType<FakeModuleRegistration>()];

    private static List<string> RegisteredIds(IServiceCollection services) =>
        [.. Registrations(services).Select(r => r.PluginId)];
}
