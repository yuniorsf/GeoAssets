using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Hosting.Abstractions.Plugins;
using Xunit;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>XD01-427 (ADR-002 D2/D3): <see cref="PluginLoadContext"/> over the real fixture plugin assemblies.</summary>
public sealed class PluginLoadContextTests : IDisposable
{
    private readonly TempPluginDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ThePluginsEntryAssembly_LoadsIntoItsOwnContext_NotTheHosts()
    {
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));

        var assembly = context.LoadPluginAssembly();

        AssemblyLoadContext.GetLoadContext(assembly).Should().BeSameAs(context);
        context.PluginMainAssemblyPath.Should().Be(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));
        context.PluginDirectory.Should().Be(PluginFixtures.Folder(PluginFixtures.Alpha));
        context.IsCollectible.Should().BeFalse();
        context.Name.Should().Be("GeoAssets.ProviderPlugin:" + PluginFixtures.Alpha);
        AssemblyLoadContext.Default.Assemblies.Should().NotContain(assembly);
    }

    [Fact]
    public void TheModuleImplementsTheHostsIProviderPluginModule_TheContractsAreNotLoadedAgain()
    {
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));

        var moduleType = ProviderPluginModuleScanner.Scan(context.LoadPluginAssembly()).Single().ModuleType;
        var module = Activator.CreateInstance(moduleType);

        moduleType.GetInterfaces().Should().Contain(typeof(IProviderPluginModule));
        module.Should().BeAssignableTo<IProviderPluginModule>().Which.PluginId.Should().Be("fixture-alpha");
        AssemblyLoadContext.GetLoadContext(typeof(IProviderPluginModule).Assembly).Should().BeSameAs(AssemblyLoadContext.Default);
        LoadedNames(context).Should().NotContain(n => string.Equals(n, "GeoAssets.Core", StringComparison.Ordinal) || n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
    }

    [Fact]
    public void AContractCopyInThePluginsFolder_IsNeverLoaded_TheHostsIsUsed()
    {
        // Without a .deps.json every assembly in the folder is the plugin's; a copy of GeoAssets.Core among them must
        // still lose to the host's, or IProviderPluginModule would be two types.
        var folder = _temp.CopyPlugin(PluginFixtures.Alpha, withDepsJson: false);
        var contract = typeof(IProviderPluginModule).Assembly;
        File.Copy(contract.Location, Path.Combine(folder, Path.GetFileName(contract.Location)));
        var context = new PluginLoadContext(Path.Combine(folder, PluginFixtures.Alpha + ".dll"));

        var moduleType = ProviderPluginModuleScanner.Scan(context.LoadPluginAssembly()).Single().ModuleType;

        moduleType.GetInterfaces().Should().Contain(typeof(IProviderPluginModule));
        LoadedNames(context).Should().NotContain("GeoAssets.Core");
    }

    [Fact]
    public void EachPluginLoadsItsOwnVersionOfAPrivateDependency_SideBySide()
    {
        var alpha = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));
        var beta = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Beta));

        PluginFixtures.InvokeModuleStatic(alpha, PluginFixtures.Alpha, "PrivateDependencyEdition").Should().Be("v1");
        PluginFixtures.InvokeModuleStatic(beta, PluginFixtures.Beta, "PrivateDependencyEdition").Should().Be("v2");

        PrivateDependency(alpha).GetName().Version.Should().Be(new Version(1, 0, 0, 0));
        PrivateDependency(beta).GetName().Version.Should().Be(new Version(2, 0, 0, 0));
        PrivateDependency(alpha).Location.Should().StartWith(alpha.PluginDirectory);
        PrivateDependency(beta).Location.Should().StartWith(beta.PluginDirectory);
        AssemblyLoadContext.Default.Assemblies.Should().NotContain(a => string.Equals(a.GetName().Name, PluginFixtures.PrivateDependency, StringComparison.Ordinal));
    }

    [Fact]
    public void ANativeLibrary_ResolvesFromThePluginsRuntimesFolder_ThroughItsDepsJson()
    {
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));

        var path = context.ResolveUnmanagedDllPath("e_sqlite3");

        path.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();
        path.Should().StartWith(Path.Combine(context.PluginDirectory, "runtimes"));
        Path.GetDirectoryName(path).Should().EndWith(Path.DirectorySeparatorChar + "native");
    }

    [Fact]
    public void ANativeLibrary_ResolvesFromRuntimesRidNative_WithoutADepsJson()
    {
        var folder = _temp.CopyPlugin(PluginFixtures.Alpha, withDepsJson: false);
        var context = new PluginLoadContext(Path.Combine(folder, PluginFixtures.Alpha + ".dll"));

        var path = context.ResolveUnmanagedDllPath("e_sqlite3");

        path.Should().NotBeNull();
        path.Should().StartWith(Path.Combine(folder, "runtimes"));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void ThePluginsPInvoke_LoadsItsNativeLibrary_ThroughLoadUnmanagedDll()
    {
        // libe_sqlite3 is nowhere the runtime's default probing looks (not next to the test host, not a system
        // library): the call only succeeds because the plugin's context resolved it from the plugin's folder.
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));

        var version = PluginFixtures.InvokeModuleStatic(context, PluginFixtures.Alpha, "NativeLibraryVersion");

        version.Should().BeOfType<int>().Which.Should().BeGreaterThanOrEqualTo(3_000_000);
    }

    [Fact]
    public void ANativeLibraryItLoaded_IsListed_WithItsPathInThePluginsFolder()
    {
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));
        context.LoadedNativeLibraries.Should().BeEmpty();

        PluginFixtures.InvokeModuleStatic(context, PluginFixtures.Alpha, "NativeLibraryVersion");

        context.LoadedNativeLibraries.Should().ContainSingle()
            .Which.Should().StartWith(Path.Combine(context.PluginDirectory, "runtimes"))
            .And.Contain("e_sqlite3");
    }

    [Fact]
    public void AnUnknownNativeLibrary_IsLeftToTheRuntime()
    {
        var context = new PluginLoadContext(PluginFixtures.EntryAssembly(PluginFixtures.Alpha));

        context.ResolveUnmanagedDllPath("not-a-native-library").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPath_IsRejected(string path)
    {
        var act = () => new PluginLoadContext(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AMissingEntryAssembly_IsRejected()
    {
        var missing = Path.Combine(_temp.Root, "Missing", "Missing.dll");

        var act = () => new PluginLoadContext(missing);

        act.Should().Throw<FileNotFoundException>().WithMessage($"*'{missing}' does not exist*");
    }

    [Theory]
    [InlineData("GeoAssets.Core", true)]
    [InlineData("geoassets.identity", true)]
    [InlineData("GeoAssets.Hosting.Abstractions", true)]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions", true)]
    [InlineData("Microsoft.Extensions.Http.Resilience", true)]
    [InlineData("GeoAssets.Provider.Rest", false)]
    [InlineData("NetTopologySuite", false)]
    [InlineData("Microsoft.Extension", false)]
    public void TheSharedContracts_AreTheGeoAssetsContractsAndMicrosoftExtensions(string name, bool shared)
    {
        PluginLoadContext.IsSharedContract(new AssemblyName(name)).Should().Be(shared);
    }

    [Fact]
    public void TheSharedContractAssemblies_AreListed()
    {
        PluginLoadContext.SharedContractAssemblies.Should().BeEquivalentTo(
            "GeoAssets.Core", "GeoAssets.Identity", "GeoAssets.Hosting.Abstractions");
    }

    [Theory]
    [InlineData("GeoAssets.Core, Version=9.0.0.0", "1.0.0.0", true)]
    [InlineData("Microsoft.Extensions.Logging.Abstractions, Version=99.0.0.0", "10.0.0.0", true)]
    [InlineData("NetTopologySuite, Version=2.0.0.0", "2.0.0.0", true)]
    [InlineData("NetTopologySuite, Version=2.0.0.0", "2.6.0.0", true)]
    [InlineData("NetTopologySuite, Version=3.0.0.0", "2.6.0.0", false)]
    [InlineData("NetTopologySuite", "2.6.0.0", true)]
    [InlineData("GeoAssets.Core, Version=1.0.0.0", null, false)]
    [InlineData("SomePrivateLibrary, Version=1.0.0.0", null, false)]
    public void TheHostsCopy_IsUsedForContractsAlways_AndForOthersWhenNotOlder(string requested, string? hostVersion, bool usesHost)
    {
        var host = hostVersion is null ? null : Version.Parse(hostVersion);

        PluginLoadContext.UsesHostCopy(new AssemblyName(requested), host).Should().Be(usesHost);
    }

    [Fact]
    public void TheHostVersion_ComesFromTheHostsAssemblies()
    {
        PluginLoadContext.HostVersion(new AssemblyName("GeoAssets.Core")).Should().Be(typeof(IProviderPluginModule).Assembly.GetName().Version);
        PluginLoadContext.HostVersion(new AssemblyName("System.Runtime")).Should().NotBeNull();
        PluginLoadContext.HostVersion(new AssemblyName(PluginFixtures.PrivateDependency)).Should().BeNull();
    }

    private static List<string> LoadedNames(PluginLoadContext context) =>
        [.. context.Assemblies.Select(a => a.GetName().Name ?? string.Empty)];

    private static Assembly PrivateDependency(PluginLoadContext context) =>
        context.Assemblies.Single(a => string.Equals(a.GetName().Name, PluginFixtures.PrivateDependency, StringComparison.Ordinal));
}
