using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// XD01-423 — <see cref="ProviderConfigField.GetAcceptedExtensions"/> turns a field's HTML-style
/// <see cref="ProviderConfigField.Accept"/> filter into the extension list a native file picker filters by.
/// </summary>
public class ProviderConfigFieldAcceptTests
{
    private static ProviderConfigField Field(string? accept) =>
        new("file", "File", ProviderFieldType.BinaryFile, Accept: accept);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(", ,")]
    [InlineData(".")]
    [InlineData("application/zip,image/*")]
    public void NoDeclaredExtension_MeansAnyFile(string? accept) =>
        Field(accept).GetAcceptedExtensions().Should().BeNull();

    [Fact]
    public void ASingleExtension_IsReturnedAsIs() =>
        Field(".zip").GetAcceptedExtensions().Should().Equal(".zip");

    [Fact]
    public void SeveralExtensions_AreTrimmedAndKeptInOrder() =>
        Field(" .pmtiles , .fgb,.parquet ,.TIF").GetAcceptedExtensions()
            .Should().Equal(".pmtiles", ".fgb", ".parquet", ".TIF");

    [Fact]
    public void AnExtensionWithoutItsDot_GetsOne() =>
        Field("zip,..gpkg").GetAcceptedExtensions().Should().Equal(".zip", ".gpkg");

    [Fact]
    public void DuplicateExtensions_IgnoringCase_AreReturnedOnce() =>
        Field(".zip,.ZIP,zip").GetAcceptedExtensions().Should().Equal(".zip");

    [Fact]
    public void MimeTypeEntries_AreSkipped() =>
        Field("application/zip,.zip,image/*").GetAcceptedExtensions().Should().Equal(".zip");
}
