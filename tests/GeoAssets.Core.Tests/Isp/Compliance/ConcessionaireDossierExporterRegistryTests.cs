using FluentAssertions;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance;

/// <summary>Covers <see cref="ConcessionaireDossierExporterRegistry"/>'s name resolution, independent of the real exporters.</summary>
public class ConcessionaireDossierExporterRegistryTests
{
    private sealed class FakeExporter(string distributorName) : IConcessionaireDossierExporter
    {
        public string DistributorName => distributorName;

        public ConcessionaireDossierResult Export(IReadOnlyList<GeoFeature> poleFeatures) =>
            throw new NotSupportedException();
    }

    private static ConcessionaireDossierExporterRegistry BuildRegistry() =>
        new([new FakeExporter("Neoenergia"), new FakeExporter("Enel")]);

    [Theory]
    [InlineData("Neoenergia")]
    [InlineData("neoenergia")]
    [InlineData("NEOENERGIA")]
    public void TryResolve_CaseInsensitiveNeoenergiaLikeName_ResolvesIt(string name)
    {
        var registry = BuildRegistry();

        var resolved = registry.TryResolve(name, out var exporter);

        resolved.Should().BeTrue();
        exporter!.DistributorName.Should().Be("Neoenergia");
    }

    [Theory]
    [InlineData("Enel")]
    [InlineData("enel")]
    [InlineData("ENEL")]
    public void TryResolve_CaseInsensitiveEnelLikeName_ResolvesIt(string name)
    {
        var registry = BuildRegistry();

        var resolved = registry.TryResolve(name, out var exporter);

        resolved.Should().BeTrue();
        exporter!.DistributorName.Should().Be("Enel");
    }

    [Fact]
    public void TryResolve_UnknownDistributorName_ReturnsFalse()
    {
        var registry = BuildRegistry();

        var resolved = registry.TryResolve("Unknown Distributor", out var exporter);

        resolved.Should().BeFalse();
        exporter.Should().BeNull();
    }

    [Fact]
    public void RegisteredDistributors_ListsBothNames()
    {
        var registry = BuildRegistry();

        registry.RegisteredDistributors.Should().BeEquivalentTo(["Neoenergia", "Enel"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_NullEmptyOrWhitespaceDistributorName_ThrowsArgumentException(string? name)
    {
        var registry = BuildRegistry();

        var act = () => registry.TryResolve(name!, out _);

        act.Should().Throw<ArgumentException>();
    }
}
