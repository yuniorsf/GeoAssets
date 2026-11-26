using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>
/// XD01-425 (ADR-002 D1): each spatial provider's <see cref="IProviderPluginModule"/> declares its id and hosts,
/// can be instantiated by discovery, and registers exactly what the provider's registration extension registers —
/// so a host gets the same services whether it calls the extension or runs the module.
/// </summary>
public sealed class ProviderPluginModuleTests
{
    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void Module_DeclaresItsPluginIdAndSupportedHosts(string pluginId)
    {
        var expected = ProviderModuleCases.ById[pluginId];

        var module = ProviderModuleCases.Create(pluginId);

        module.PluginId.Should().Be(expected.PluginId);
        module.SupportedHosts.Should().Be(expected.SupportedHosts);
    }

    [Fact]
    public void PostgresModule_IsServerOnly()
    {
        var module = ProviderModuleCases.Create("postgres");

        module.SupportedHosts.Should().Be(ProviderHostTargets.Server,
            "only GeoAssets.Server opens the core asset database (Invariant 1)");
        module.SupportedHosts.HasFlag(ProviderHostTargets.Browser).Should().BeFalse();
        module.SupportedHosts.HasFlag(ProviderHostTargets.MobileDesktop).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void Module_IsAPublicConcreteTypeWithAPublicParameterlessConstructor(string pluginId)
    {
        var type = ProviderModuleCases.ById[pluginId].ModuleType;

        type.IsPublic.Should().BeTrue("discovery scans public module types");
        type.IsAbstract.Should().BeFalse();
        type.IsSealed.Should().BeTrue();
        type.GetConstructor(Type.EmptyTypes).Should().NotBeNull("discovery instantiates modules without arguments");
    }

    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void RegisterServices_RegistersExactlyWhatTheExtensionRegisters(string pluginId)
    {
        var expected = ProviderModuleCases.ById[pluginId];

        var viaExtension = new ServiceCollection();
        expected.Extension(viaExtension);

        var viaModule = new ServiceCollection();
        ProviderModuleCases.Create(pluginId).RegisterServices(viaModule, EmptyConfiguration());

        viaExtension.Should().NotBeEmpty();
        Signatures(viaModule).Should().Equal(Signatures(viaExtension));
    }

    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void RegisterServices_RegistersTheProvidersPluginUnderTheModulesId(string pluginId)
    {
        var expected = ProviderModuleCases.ById[pluginId];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var module = ProviderModuleCases.Create(pluginId);
        module.RegisterServices(services, EmptyConfiguration());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var plugin = provider.GetServices<IProviderPlugin>().Should().ContainSingle().Subject;
        plugin.Should().BeOfType(expected.PluginType);
        plugin.Id.Should().Be(module.PluginId, "a module and the provider plugin it registers share one id");
    }

    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void RegisterServices_ReadsNothingFromTheConfigurationItIsGiven(string pluginId)
    {
        var configuration = new ThrowingConfiguration();
        var services = new ServiceCollection();

        var register = () => ProviderModuleCases.Create(pluginId).RegisterServices(services, configuration);

        register.Should().NotThrow("the provider's registration takes no settings; it reads none from the configuration");
        services.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ProviderModuleCases.Ids), MemberType = typeof(ProviderModuleCases))]
    public void RegisterServices_RejectsNullArguments(string pluginId)
    {
        var module = ProviderModuleCases.Create(pluginId);

        module.Invoking(m => m.RegisterServices(null!, EmptyConfiguration()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        module.Invoking(m => m.RegisterServices(new ServiceCollection(), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static List<string> Signatures(IServiceCollection services) =>
        services.Select(Signature).ToList();

    private static string Signature(ServiceDescriptor d)
    {
        var implementationType = d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;
        var instance = d.IsKeyedService ? d.KeyedImplementationInstance : d.ImplementationInstance;
        var factory = d.IsKeyedService ? (Delegate?)d.KeyedImplementationFactory : d.ImplementationFactory;
        return string.Join(
            " | ",
            d.ServiceType.FullName,
            d.ServiceKey?.ToString() ?? "-",
            d.Lifetime.ToString(),
            implementationType?.FullName ?? "-",
            instance?.GetType().FullName ?? "-",
            factory is null ? "-" : $"{factory.Method.DeclaringType?.FullName}.{factory.Method.Name}");
    }

    /// <summary>A configuration that fails any read, so a module that reads one fails the test.</summary>
    private sealed class ThrowingConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => throw Read();
            set => throw Read();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => throw Read();

        public IChangeToken GetReloadToken() => throw Read();

        public IConfigurationSection GetSection(string key) => throw Read();

        private static InvalidOperationException Read() =>
            new("The module read its configuration.");
    }
}
