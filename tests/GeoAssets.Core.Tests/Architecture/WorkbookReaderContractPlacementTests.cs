using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Xunit;

namespace GeoAssets.Core.Tests.Architecture;

/// <summary>
/// XD01-423 / ADR-002 D3 — <see cref="IWorkbookReader"/> is a shared contract: it lives in GeoAssets.Core (the
/// default load context), so its consumers never reference the ClosedXML-backed provider that implements it.
/// </summary>
[Trait("Category", "Architecture")]
public class WorkbookReaderContractPlacementTests
{
    [Fact]
    public void IWorkbookReader_LivesInGeoAssetsCore()
    {
        typeof(IWorkbookReader).Assembly.Should().BeSameAs(typeof(IAssetProvider).Assembly);
        typeof(IWorkbookReader).Namespace.Should().Be("GeoAssets.Core.Interfaces");
    }

    [Fact]
    public void IWorkbookReader_IsDefinedOnlyOnce()
    {
        var assemblies = new List<Assembly> { typeof(IAssetProvider).Assembly };
        assemblies.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "GeoAssets.*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))));

        var defined = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => string.Equals(type.Name, nameof(IWorkbookReader), StringComparison.Ordinal))
            .Select(type => type.FullName);

        defined.Should().Equal(typeof(IWorkbookReader).FullName);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceClosedXml()
    {
        typeof(IWorkbookReader).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Should().NotContain(name => name.StartsWith("ClosedXML", StringComparison.Ordinal),
                "the workbook contract is format-neutral; only GeoAssets.Provider.Xlsx depends on ClosedXML");
    }
}
