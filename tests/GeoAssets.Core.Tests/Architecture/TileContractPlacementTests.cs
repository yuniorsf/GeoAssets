using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Architecture;

/// <summary>
/// XD01-431 / ADR-002 D3 — the tile and LOD contracts cross plugin boundaries, so they live in GeoAssets.Core (the
/// default load context), once, and GeoAssets.Core gains no rendering, caching or web engine for them.
/// </summary>
[Trait("Category", "Architecture")]
public class TileContractPlacementTests
{
    private static readonly Type[] SharedContracts =
    [
        typeof(TileAddress),
        typeof(TileRange),
        typeof(TileLayerSet),
        typeof(TileRenderTier),
        typeof(TileRequest),
        typeof(TilePayload),
        typeof(TileFormat),
        typeof(IVectorTileSource),
        typeof(IRasterTileSource),
        typeof(ILodPolicy),
        typeof(LodThresholds),
        typeof(ITileCache),
        typeof(TileCacheKey),
        typeof(TileCacheScope),
        typeof(ITileInvalidation),
        typeof(DirtyTileRegion),
    ];

    public static TheoryData<Type> Contracts() => [.. SharedContracts];

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Contract_LivesInGeoAssetsCoreRendering(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Assembly.Should().BeSameAs(typeof(IAssetProvider).Assembly);
        contract.Namespace.Should().Be("GeoAssets.Core.Rendering");
        contract.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void Contracts_AreDefinedOnlyOnce()
    {
        var assemblies = new List<Assembly> { typeof(IAssetProvider).Assembly };
        assemblies.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "GeoAssets.*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))));
        var names = SharedContracts.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var defined = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && names.Contains(type.Name))
            .Select(type => type.FullName);

        defined.Should().BeEquivalentTo(SharedContracts.Select(t => t.FullName));
    }

    [Fact]
    public void Ports_AreAsyncAndCancellable()
    {
        foreach (var port in new[] { typeof(IVectorTileSource), typeof(IRasterTileSource), typeof(ITileCache), typeof(ITileInvalidation) })
        {
            foreach (var method in port.GetMethods())
            {
                typeof(Task).IsAssignableFrom(method.ReturnType).Should().BeTrue("{0}.{1} crosses an I/O boundary", port.Name, method.Name);
                method.GetParameters().Last().ParameterType.Should().Be<CancellationToken>("{0}.{1} is cancellable", port.Name, method.Name);
            }
        }
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceATileEngine()
    {
        var referenced = typeof(TileAddress).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

        referenced.Should().NotContain(
            name => name.StartsWith("SkiaSharp", StringComparison.Ordinal)
                    || name.StartsWith("MapLibre", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.Extensions.Caching", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
                    || name.StartsWith("MaxRev.Gdal", StringComparison.Ordinal)
                    || name.StartsWith("Npgsql", StringComparison.Ordinal),
            "the tile/LOD contracts are engine-agnostic; SkiaSharp, PostGIS, GDAL, caches and web stacks stay in adapters");
    }
}
