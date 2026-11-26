using System.Reflection;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Core.Tests.Plugins;

/// <summary>
/// Fake provider plugin modules and sources for the XD01-426 discovery tests. All internal, so a scan skips them —
/// scanning public modules is tested over the real provider modules (<c>GeoAssets.Provider.PluginModules.Tests</c>).
/// </summary>
internal static class FakePluginModules
{
    /// <summary>What a fake module registered, and with which configuration — the order of these is the registration order.</summary>
    internal sealed record FakeModuleRegistration(string PluginId, IConfiguration Configuration);

    /// <summary>A fake module: registers a <see cref="FakeModuleRegistration"/> and an <see cref="IProviderPlugin"/> under its id.</summary>
    internal abstract class FakeModuleBase(string pluginId, ProviderHostTargets supportedHosts) : IProviderPluginModule
    {
        public string PluginId => pluginId;

        public ProviderHostTargets SupportedHosts => supportedHosts;

        public virtual void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(new FakeModuleRegistration(pluginId, configuration));
            services.AddSingleton<IProviderPlugin>(new FakeModulePlugin(pluginId));
        }
    }

    internal sealed class AlphaModule() : FakeModuleBase("alpha", ProviderHostTargets.All);

    internal sealed class BetaServerModule() : FakeModuleBase("beta", ProviderHostTargets.Server);

    /// <summary>Upper-case id, to prove ordering and matching ignore case.</summary>
    internal sealed class CharlieModule() : FakeModuleBase("Charlie", ProviderHostTargets.All);

    internal sealed class DeltaClientModule() : FakeModuleBase("delta", ProviderHostTargets.Browser | ProviderHostTargets.MobileDesktop);

    internal sealed class NoHostModule() : FakeModuleBase("nohost", ProviderHostTargets.None);

    /// <summary>Same id as <see cref="AlphaModule"/>, differently cased.</summary>
    internal sealed class AlphaDuplicateModule() : FakeModuleBase("ALPHA", ProviderHostTargets.All);

    internal sealed class BlankIdModule() : FakeModuleBase("  ", ProviderHostTargets.All);

    internal sealed class ThrowingConstructorModule : FakeModuleBase
    {
        public ThrowingConstructorModule()
            : base("throwing-ctor", ProviderHostTargets.All) =>
            throw new InvalidOperationException("constructor boom");
    }

    internal sealed class NoParameterlessConstructorModule(int unused) : FakeModuleBase($"no-ctor-{unused}", ProviderHostTargets.All);

    internal sealed class ThrowingRegistrationModule() : FakeModuleBase("faulty", ProviderHostTargets.All)
    {
        public override void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
            throw new ArgumentException("registration boom", nameof(configuration));
    }

    internal sealed class GenericModule<T>() : FakeModuleBase($"generic-{typeof(T).Name}", ProviderHostTargets.All);

    internal sealed class InternalModule() : FakeModuleBase("internal", ProviderHostTargets.All);

    internal sealed class FakeModulePlugin(string id) : IProviderPlugin
    {
        public string Id => id;
        public string DisplayName => id;
        public string Description => string.Empty;
        public IReadOnlyList<ProviderConfigField> ConfigFields => [];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Base of the test sources, counting how often discovery reads them.</summary>
    internal abstract class CountingSourceBase(string name) : IPluginAssemblySource
    {
        public string Name => name;

        public int Reads { get; private set; }

        public IReadOnlyList<ProviderPluginModuleDescriptor> GetModuleTypes()
        {
            Reads++;
            return ModuleTypes();
        }

        protected abstract IReadOnlyList<ProviderPluginModuleDescriptor> ModuleTypes();
    }

    /// <summary>A source handing back exactly the given module types.</summary>
    internal sealed class FakeSource(string name, params Type[] moduleTypes) : CountingSourceBase(name)
    {
        protected override IReadOnlyList<ProviderPluginModuleDescriptor> ModuleTypes() =>
            [.. moduleTypes.Select(t => new ProviderPluginModuleDescriptor(t))];
    }

    /// <summary>A source failing like a directory source whose assembly has a type that can't load.</summary>
    internal sealed class TypeLoadFailingSource() : CountingSourceBase("broken-dir")
    {
        protected override IReadOnlyList<ProviderPluginModuleDescriptor> ModuleTypes() =>
            throw new ReflectionTypeLoadException([null], [new FileNotFoundException("Could not load 'Missing.Dependency'.")]);
    }

    internal sealed class NullListSource() : CountingSourceBase("null-list")
    {
        protected override IReadOnlyList<ProviderPluginModuleDescriptor> ModuleTypes() => null!;
    }

    /// <summary>An assembly whose <see cref="Assembly.GetTypes"/> answers the given types, or throws the given exception.</summary>
    internal sealed class FakeAssembly(Type[] types, Exception? failure = null) : Assembly
    {
        public override string FullName => "Fake.Plugins, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public override Type[] GetTypes() => failure is null ? types : throw failure;
    }
}
