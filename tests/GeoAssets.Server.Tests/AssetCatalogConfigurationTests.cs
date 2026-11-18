using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The catalog instance id this host serves (XD01-357): the new key, the deprecated key it falls
/// back to, and the default.
/// </summary>
public sealed class AssetCatalogConfigurationTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void InstanceId_NewKey_Wins_Trimmed()
    {
        var configuration = Configuration(
            (AssetCatalogConfiguration.InstanceIdKey, " east "),
            (AssetCatalogConfiguration.LegacyInstanceIdKey, "west"));

        AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey).Should().Be("east");
        fromLegacyKey.Should().BeFalse();
    }

    [Fact]
    public void InstanceId_OnlyLegacyKey_FallsBackToIt_AndFlagsIt()
    {
        var configuration = Configuration((AssetCatalogConfiguration.LegacyInstanceIdKey, " west "));

        AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey).Should().Be("west");
        fromLegacyKey.Should().BeTrue();
    }

    [Fact]
    public void InstanceId_BlankNewKey_FallsBackToLegacyKey()
    {
        var configuration = Configuration(
            (AssetCatalogConfiguration.InstanceIdKey, "  "),
            (AssetCatalogConfiguration.LegacyInstanceIdKey, "west"));

        AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey).Should().Be("west");
        fromLegacyKey.Should().BeTrue();
    }

    [Fact]
    public void InstanceId_NeitherKey_IsDefault()
    {
        AssetCatalogConfiguration.InstanceId(Configuration(), out var fromLegacyKey)
            .Should().Be(AssetCatalogConfiguration.DefaultInstanceId);
        fromLegacyKey.Should().BeFalse();
        AssetCatalogConfiguration.InstanceId(Configuration()).Should().Be("default");
    }

    [Fact]
    public void InstanceId_NullConfiguration_Throws()
    {
        ((Action)(() => AssetCatalogConfiguration.InstanceId(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => AssetCatalogConfiguration.InstanceId(null!, out _))).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("east", null)]
    [InlineData(null, "west")]
    [InlineData("east", "west")]
    [InlineData(null, null)]
    public void OrderOccupancyCatalogInstanceId_AgreesWithTheHostsCatalogInstanceId(string? newKey, string? legacyKey)
    {
        var configuration = Configuration(
            (AssetCatalogConfiguration.InstanceIdKey, newKey),
            (AssetCatalogConfiguration.LegacyInstanceIdKey, legacyKey));

        OrderOccupancyHostExtensions.CatalogInstanceId(configuration)
            .Should().Be(AssetCatalogConfiguration.InstanceId(configuration));
    }
}
