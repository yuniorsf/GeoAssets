using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Hosting.Abstractions.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.Gdal.PluginLoading.Tests;

/// <summary>
/// XD01-437 (ADR-002 D2/D5): the GDAL plugin loaded the way a server loads a drop-in — from <c>plugins/providers/</c>,
/// by <see cref="DirectoryPluginAssemblySource"/>, in its own <see cref="PluginLoadContext"/> — in a host with no static
/// copy of it: managed GDAL loads in that context only, native GDAL resolves through its <c>LoadUnmanagedDll</c> from
/// the plugin's folder, <c>GdalBase.ConfigureAll()</c> runs once under concurrent first use, the plugin's ports work
/// across the context boundary, and a second copy of the plugin is refused before it touches GDAL. These need the
/// native runtime the plugin ships (macOS/Linux, x64/arm64); elsewhere they are skipped, never passed.
/// </summary>
public sealed class GdalPluginLoadContextTests
{
    private static GdalPluginHost Host => GdalPluginHost.Instance;

    [NativeGdalFact]
    public void ThePlugin_LoadsInItsOwnContext_AndManagedGdalOnlyThere()
    {
        var context = Host.Plugin.LoadContext;

        AssemblyLoadContext.GetLoadContext(Host.Plugin.Assembly).Should().BeSameAs(context);
        Host.Plugin.Modules.Select(m => m.ModuleType.FullName).Should().Equal("GeoAssets.Provider.Gdal.GdalCloudNativePluginModule");
        context.Assemblies.Select(a => a.GetName().Name).Should().Contain("MaxRev.Gdal.Core");
        AssemblyLoadContext.Default.Assemblies.Select(a => a.GetName().Name).Should().NotContain(["MaxRev.Gdal.Core", GdalPluginHost.PluginName]);
        context.Assemblies.Select(a => a.GetName().Name).Should().NotContain("GeoAssets.Core", "the shared contracts come from the host (D3)");
    }

    [NativeGdalFact]
    public void ConcurrentFirstUse_ConfiguresNativeGdalExactlyOnce()
    {
        Host.FirstUseResults.Should().HaveCount(GdalPluginHost.Threads).And.OnlyContain(available => available);
        GdalPluginHost.Initializer(Host.Plugin.Assembly, "ConfigurationCount").Should().Be(1);
        GdalPluginHost.Initializer(Host.Plugin.Assembly, "IsInitialized").Should().Be(true);
        ((string?)GdalPluginHost.Initializer(Host.Plugin.Assembly, "NativeVersion")).Should().StartWith("3.");

        GdalPluginHost.Initializer(Host.Plugin.Assembly, "EnsureInitialized");

        GdalPluginHost.Initializer(Host.Plugin.Assembly, "ConfigurationCount").Should().Be(1, "a later call never configures again");
    }

    [NativeGdalFact]
    public void NativeGdal_ResolvesThroughThePluginsLoadUnmanagedDll_FromItsFolder()
    {
        var context = Host.Plugin.LoadContext;

        context.LoadedNativeLibraries.Should().Contain(
            path => Path.GetFileName(path).Contains("gdal_wrap", StringComparison.Ordinal)
                    && path.StartsWith(context.PluginDirectory, StringComparison.Ordinal),
            "the OSGeo bindings' DllImport(\"gdal_wrap\") is answered by the plugin's .deps.json native assets");
    }

    [NativeGdalFact]
    public void NativeGdal_IsOwnedByThePluginsCopy_InTheSharedRegistry()
    {
        var owner = NativeLibraryOwnership.OwnerOf("gdal");

        owner.Should().BeSameAs(Host.Plugin.Assembly);
        AssemblyLoadContext.GetLoadContext(owner!).Should().BeSameAs(Host.Plugin.LoadContext);
    }

    [NativeGdalFact]
    public void ThePluginsPorts_WorkAcrossTheContextBoundary()
    {
        var export = Host.Services.GetRequiredService<IFileGeodatabaseExportService>();
        var reader = Host.Services.GetRequiredService<IVectorFileReader>();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [new GeoFeature { Geometry = new GeoPoint(-38.5, -12.9), Properties = new GeoFeatureProperties { Name = "Pole 1" } }],
        };

        var zip = export.ExportToZip(layers, Wgs84Wkt, 4326);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        archive.Entries.Should().Contain(e => e.Name.EndsWith(".gdbtable", StringComparison.Ordinal));
        reader.IsAvailable(VectorFileFormat.GeoParquet).Should().BeTrue("the shipped runtime has OGR's Parquet driver");
        AssemblyLoadContext.GetLoadContext(export.GetType().Assembly).Should().BeSameAs(Host.Plugin.LoadContext);
    }

    [NativeGdalFact]
    public void ASecondCopyOfThePlugin_IsRefused_BeforeItTouchesGdal()
    {
        var secondContext = new PluginLoadContext(Host.Plugin.LoadContext.PluginMainAssemblyPath);
        var secondCopy = secondContext.LoadPluginAssembly();
        secondCopy.Should().NotBeSameAs(Host.Plugin.Assembly);

        var act = () => GdalPluginHost.Initializer(secondCopy, "EnsureInitialized");

        var refusal = act.Should().Throw<TargetInvocationException>().WithInnerException<NativeLibraryOwnershipException>().Which;
        refusal.LibraryName.Should().Be("gdal");
        refusal.OwnerLoadContext.Should().Be(Host.Plugin.LoadContext.Name);
        GdalPluginHost.Initializer(secondCopy, "ConfigurationCount").Should().Be(0);
        secondContext.Assemblies.Select(a => a.GetName().Name).Should().NotContain("MaxRev.Gdal.Core", "the refused copy never loaded GDAL's bindings");
    }

    private const string Wgs84Wkt =
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0]," +
        "UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]";
}
