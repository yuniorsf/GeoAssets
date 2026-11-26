using System.Reflection;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// Registers the <see cref="IProviderPluginModule"/>s that <see cref="IPluginAssemblySource"/>s offer, before the
/// host's <see cref="IServiceProvider"/> is built (ADR-002 D1/D2/D7).
/// </summary>
public static class ProviderPluginRegistrationExtensions
{
    /// <summary>
    /// Discovers and registers provider plugin modules for the host <paramref name="hostTarget"/>, with no
    /// allowlist or denylist. See <see cref="AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.
    /// </summary>
    public static IServiceCollection AddDiscoveredProviderPlugins(
        this IServiceCollection services,
        IConfiguration configuration,
        ProviderHostTargets hostTarget,
        params IPluginAssemblySource[] sources) =>
        services.AddDiscoveredProviderPlugins(
            configuration, new ProviderPluginDiscoveryOptions { HostTarget = hostTarget }, sources);

    /// <summary>
    /// Discovers the modules <paramref name="sources"/> offer and registers the ones this host runs:
    /// <list type="number">
    /// <item>creates every module each source offers (sources in the order given) through its public parameterless constructor;</item>
    /// <item>fails if two of them share a <see cref="IProviderPluginModule.PluginId"/> (case-insensitive), whether or not they would be skipped;</item>
    /// <item>skips — without failing — a module whose <see cref="IProviderPluginModule.SupportedHosts"/> doesn't include
    /// <see cref="ProviderPluginDiscoveryOptions.HostTarget"/>, one outside a non-empty
    /// <see cref="ProviderPluginDiscoveryOptions.EnabledPluginIds"/> and one in <see cref="ProviderPluginDiscoveryOptions.DisabledPluginIds"/>;</item>
    /// <item>fails if a module left to register has an id an earlier call already registered;</item>
    /// <item>runs <see cref="IProviderPluginModule.RegisterServices"/> with <paramref name="configuration"/> for each of
    /// them in <see cref="IProviderPluginModule.PluginId"/> order (ordinal, ignoring case) — deterministic whatever
    /// order the sources list their modules in;</item>
    /// <item>registers <see cref="ProviderPluginRegistry"/> if the host hasn't.</item>
    /// </list>
    /// Nothing is registered when it fails before the modules register.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="ProviderPluginDiscoveryOptions.HostTarget"/> isn't exactly one host, no source is given or a source is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A source can't read an assembly's types (a <see cref="ReflectionTypeLoadException"/>), a module can't be
    /// created, has a blank id, duplicates another's id or fails to register its services.
    /// </exception>
    public static IServiceCollection AddDiscoveredProviderPlugins(
        this IServiceCollection services,
        IConfiguration configuration,
        ProviderPluginDiscoveryOptions options,
        params IPluginAssemblySource[] sources)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sources);
        ValidateHostTarget(options.HostTarget);
        if (sources.Length == 0)
            throw new ArgumentException("Give at least one provider plugin source.", nameof(sources));
        if (sources.Any(s => s is null))
            throw new ArgumentException("A provider plugin source is null.", nameof(sources));

        var toRegister = Discover(sources)
            .Where(m => IsIncluded(m.Module, options))
            .OrderBy(m => m.Module.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ledger = FindLedger(services);
        foreach (var module in toRegister)
        {
            if (ledger is not null && ledger.TryGetValue(module.Module.PluginId, out var earlier))
                throw DuplicateAcrossCalls(module, earlier);
        }

        if (ledger is null)
        {
            var added = new DiscoveryLedger();
            services.AddSingleton(added);
            ledger = added.Registered;
        }

        foreach (var module in toRegister)
        {
            Register(module, services, configuration);
            ledger.Add(module.Module.PluginId, module);
        }

        services.TryAddSingleton<ProviderPluginRegistry>();
        return services;
    }

    private static void ValidateHostTarget(ProviderHostTargets hostTarget)
    {
        if (hostTarget is not (ProviderHostTargets.Server or ProviderHostTargets.Browser or ProviderHostTargets.MobileDesktop))
        {
            throw new ArgumentException(
                $"{nameof(ProviderPluginDiscoveryOptions)}.{nameof(ProviderPluginDiscoveryOptions.HostTarget)} must name " +
                $"exactly one host ({nameof(ProviderHostTargets.Server)}, {nameof(ProviderHostTargets.Browser)} or " +
                $"{nameof(ProviderHostTargets.MobileDesktop)}); got '{hostTarget.ToString()}'.",
                nameof(hostTarget));
        }
    }

    private static List<DiscoveredModule> Discover(IPluginAssemblySource[] sources)
    {
        var discovered = new List<DiscoveredModule>();
        var byId = new Dictionary<string, DiscoveredModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var descriptor in ReadModuleTypes(source))
            {
                var module = new DiscoveredModule(Create(descriptor, source), descriptor.ModuleType, source.Name);
                if (string.IsNullOrWhiteSpace(module.Module.PluginId))
                {
                    throw new InvalidOperationException(
                        $"Provider plugin module {Describe(module)} has a blank {nameof(IProviderPluginModule.PluginId)}. " +
                        "Give the module a stable, non-blank id.");
                }

                if (!byId.TryAdd(module.Module.PluginId, module))
                    throw Duplicate(module, byId[module.Module.PluginId]);
                discovered.Add(module);
            }
        }

        return discovered;
    }

    private static IReadOnlyList<ProviderPluginModuleDescriptor> ReadModuleTypes(IPluginAssemblySource source)
    {
        try
        {
            return source.GetModuleTypes()
                ?? throw new InvalidOperationException($"Provider plugin source '{source.Name}' returned no module list.");
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"Provider plugin source '{source.Name}' cannot load the types of an assembly: " +
                $"{string.Join(" ", ex.LoaderExceptions.Select(e => e?.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal))} " +
                "Its provider plugin modules can't be discovered; fix or remove the assembly.",
                ex);
        }
    }

    private static IProviderPluginModule Create(ProviderPluginModuleDescriptor descriptor, IPluginAssemblySource source)
    {
        object? instance;
        try
        {
            instance = Activator.CreateInstance(descriptor.ModuleType);
        }
        catch (TargetInvocationException ex)
        {
            throw CreationFailure(descriptor, source, ex.InnerException ?? ex);
        }
        catch (Exception ex) when (ex is MemberAccessException or TypeLoadException or IOException or BadImageFormatException or NotSupportedException)
        {
            throw CreationFailure(descriptor, source, ex);
        }

        return (IProviderPluginModule)instance!;
    }

    private static void Register(DiscoveredModule module, IServiceCollection services, IConfiguration configuration)
    {
        try
        {
            module.Module.RegisterServices(services, configuration);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Provider plugin module '{module.Module.PluginId}' {Describe(module)} failed to register its services: {ex.Message}",
                ex);
        }
    }

    private static bool IsIncluded(IProviderPluginModule module, ProviderPluginDiscoveryOptions options) =>
        module.SupportedHosts.HasFlag(options.HostTarget)
        && (options.EnabledPluginIds.Count == 0 || options.EnabledPluginIds.Contains(module.PluginId))
        && !options.DisabledPluginIds.Contains(module.PluginId);

    private static Dictionary<string, DiscoveredModule>? FindLedger(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(DiscoveryLedger)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is DiscoveryLedger existing)
            {
                return existing.Registered;
            }
        }

        return null;
    }

    private static InvalidOperationException Duplicate(DiscoveredModule module, DiscoveredModule existing) =>
        new($"Duplicate provider plugin module id '{module.Module.PluginId}': {Describe(module)} conflicts with " +
            $"{Describe(existing)} (id '{existing.Module.PluginId}'). Plugin module ids must be unique " +
            "(case-insensitive); remove one of the modules or give it another id.");

    private static InvalidOperationException DuplicateAcrossCalls(DiscoveredModule module, DiscoveredModule earlier) =>
        new($"Duplicate provider plugin module id '{module.Module.PluginId}': {Describe(module)} conflicts with " +
            $"{Describe(earlier)} (id '{earlier.Module.PluginId}'), registered by an earlier " +
            $"{nameof(AddDiscoveredProviderPlugins)} call. Plugin module ids must be unique (case-insensitive); " +
            "remove one of the modules or give it another id.");

    private static InvalidOperationException CreationFailure(
        ProviderPluginModuleDescriptor descriptor, IPluginAssemblySource source, Exception cause) =>
        new($"Provider plugin module {Describe(descriptor.ModuleType, source.Name)} cannot be created: {cause.Message} " +
            "A module needs a public parameterless constructor that doesn't throw.",
            cause);

    private static string Describe(DiscoveredModule module) => Describe(module.ModuleType, module.SourceName);

    private static string Describe(Type moduleType, string sourceName) =>
        $"{moduleType.FullName} ({moduleType.Assembly.FullName}, source '{sourceName}')";

    private sealed record DiscoveredModule(IProviderPluginModule Module, Type ModuleType, string SourceName);

    /// <summary>The modules earlier discovery calls registered in a service collection, by id.</summary>
    private sealed class DiscoveryLedger
    {
        public Dictionary<string, DiscoveredModule> Registered { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
