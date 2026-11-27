using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// XD01-438 — <see cref="ProviderConfigField.IsProvidedIn"/>, what the connect dialog checks a required field with: a file
/// field by its content, a <see cref="ProviderConfigField.PathOnly"/> binary file also by its path, any other by its key.
/// </summary>
public class ProviderConfigFieldProvidedTests
{
    private static ProviderConfig Config(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal));

    [Fact]
    public void ATextField_IsProvidedByItsKey()
    {
        var field = new ProviderConfigField("url", "URL");

        field.IsProvidedIn(Config(("url", "https://x"))).Should().BeTrue();
        field.IsProvidedIn(Config(("url", "  "))).Should().BeFalse();
        field.IsProvidedIn(Config()).Should().BeFalse();
    }

    [Theory]
    [InlineData(ProviderFieldType.File)]
    [InlineData(ProviderFieldType.BinaryFile)]
    public void AFileField_IsProvidedByItsContent_NotByItsPath(ProviderFieldType type)
    {
        var field = new ProviderConfigField("archive", "Archive", type);

        field.IsProvidedIn(Config(("archive_content", "AQID"))).Should().BeTrue();
        field.IsProvidedIn(Config(("archive_path", "/data/a.zip"))).Should().BeFalse(
            "a field that is read for its content still needs it");
    }

    [Fact]
    public void APathOnlyBinaryFile_IsProvidedByItsPath_OrItsContent()
    {
        var field = new ProviderConfigField("pack", "Pack", ProviderFieldType.BinaryFile, PathOnly: true);

        field.IsProvidedIn(Config(("pack_path", "/data/city.pmtiles"))).Should().BeTrue();
        field.IsProvidedIn(Config(("pack_content", "AQID"))).Should().BeTrue("a browser file input still reads it");
        field.IsProvidedIn(Config(("pack_name", "city.pmtiles"))).Should().BeFalse();
    }

    [Fact]
    public void PathOnly_MeansNothingForATextFile()
    {
        var field = new ProviderConfigField("geojson", "GeoJSON", ProviderFieldType.File, PathOnly: true);

        field.IsProvidedIn(Config(("geojson_path", "/data/a.geojson"))).Should().BeFalse();
    }

    [Fact]
    public void ANullConfig_IsRejected()
    {
        var act = () => new ProviderConfigField("url", "URL").IsProvidedIn(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
