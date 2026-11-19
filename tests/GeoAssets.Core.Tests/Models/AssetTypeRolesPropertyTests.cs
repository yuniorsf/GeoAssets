using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// <see cref="AssetType.Roles"/> (XD01-367): its default, null handling, and that it survives the
/// JSON every provider and the REST API use (<see cref="GeoJsonSerializer.Options"/>).
/// </summary>
public class AssetTypeRolesPropertyTests
{
    [Fact]
    public void Roles_DefaultsToEmpty() => new AssetType().Roles.Should().BeEmpty();

    [Fact]
    public void Roles_AssigningNull_StoresEmpty()
    {
        var assetType = new AssetType { Roles = ["a"] };

        assetType.Roles = null!;

        assetType.Roles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Roles_RoundTripThroughTheSharedJsonOptions()
    {
        var original = new AssetType { Name = "Pole", Roles = ["occupiable-support", "isp:pole"] };

        var json = JsonSerializer.Serialize(original, GeoJsonSerializer.Options);
        var reloaded = JsonSerializer.Deserialize<AssetType>(json, GeoJsonSerializer.Options)!;

        reloaded.Roles.Should().Equal("occupiable-support", "isp:pole");
    }

    [Fact]
    public void Roles_SerializeAsACamelCaseStringArray()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            new AssetType { Roles = ["isp:pole"] }, GeoJsonSerializer.Options));

        var roles = doc.RootElement.GetProperty("roles");
        roles.ValueKind.Should().Be(JsonValueKind.Array);
        roles.EnumerateArray().Select(e => e.GetString()).Should().Equal("isp:pole");
    }

    [Theory]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy"}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy","roles":null}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy","roles":[]}""")]
    public void Roles_MissingNullOrEmptyInJson_ReadsAsEmpty(string json)
    {
        var reloaded = JsonSerializer.Deserialize<AssetType>(json, GeoJsonSerializer.Options)!;

        reloaded.Name.Should().Be("Legacy");
        reloaded.Roles.Should().NotBeNull().And.BeEmpty();
    }
}
