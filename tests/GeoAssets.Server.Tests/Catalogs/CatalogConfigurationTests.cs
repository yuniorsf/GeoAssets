using FluentAssertions;
using GeoAssets.Server.Catalogs;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Server.Tests.Catalogs;

public sealed class CatalogConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static string Key(int index, string name) => $"{CatalogConfiguration.ProvidersKey}:{index}:{name}";

    [Fact]
    public void Constants_AreTheDocumentedKeys()
    {
        CatalogConfiguration.ProvidersKey.Should().Be("GeoAssets:Providers");
        CatalogConfiguration.DefaultCatalogId.Should().Be("primary");
    }

    [Fact]
    public void Read_WithoutProviders_ReturnsNoCatalogs()
    {
        CatalogConfiguration.Read(Config()).Should().BeEmpty();
    }

    [Fact]
    public void Read_NullConfiguration_Throws()
    {
        var act = () => CatalogConfiguration.Read(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_ReturnsTheEntriesInConfigurationOrder()
    {
        var definitions = CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres"),
            (Key(1, "Id"), "secondary"), (Key(1, "Plugin"), "rest"),
            (Key(2, "Id"), "archive"), (Key(2, "Plugin"), "wfs")));

        definitions.Select(d => (d.Id, d.PluginId)).Should().Equal(("primary", "postgres"), ("secondary", "rest"), ("archive", "wfs"));
    }

    [Fact]
    public void Read_TrimsIdPluginAndSettingsKeys()
    {
        var definitions = CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "  primary "), (Key(0, "Plugin"), " postgres  ")));

        definitions.Should().ContainSingle().Which.Should().Match<CatalogDefinition>(d => d.Id == "primary" && d.PluginId == "postgres");
    }

    [Fact]
    public void Read_SettingsKeysAreCaseInsensitive_AndNullValuesAreSkipped()
    {
        var definitions = CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres"),
            (Key(0, "Settings:connectionStringName"), "GeoAssets"),
            (Key(0, "Settings:warmUp"), null)));

        var settings = definitions.Single().Settings;
        settings.Should().ContainSingle();
        settings["CONNECTIONSTRINGNAME"].Should().Be("GeoAssets");
        settings.ContainsKey("warmUp").Should().BeFalse();
    }

    [Fact]
    public void Read_EntryWithoutSettings_HasEmptySettings()
    {
        var definitions = CatalogConfiguration.Read(Config((Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres")));

        definitions.Single().Settings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", " ")]
    public void Read_EntryWithBlankIdAndPlugin_IsIgnored(string id, string plugin)
    {
        var definitions = CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres"),
            (Key(1, "Id"), id), (Key(1, "Plugin"), plugin)));

        definitions.Select(d => d.Id).Should().Equal("primary");
    }

    [Theory]
    [InlineData("secondary", "")]
    [InlineData("", "postgres")]
    [InlineData("secondary", "  ")]
    public void Read_EntryWithOnlyOneOfIdAndPlugin_Throws(string id, string plugin)
    {
        var act = () => CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres"),
            (Key(1, "Id"), id), (Key(1, "Plugin"), plugin)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Providers:1*'Id'*'Plugin'*");
    }

    [Fact]
    public void Read_DuplicateIdIgnoringCase_Throws()
    {
        var act = () => CatalogConfiguration.Read(Config(
            (Key(0, "Id"), "primary"), (Key(0, "Plugin"), "postgres"),
            (Key(1, "Id"), "PRIMARY"), (Key(1, "Plugin"), "rest")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'PRIMARY'*more than once*");
    }

    [Fact]
    public void Read_CatalogsWithoutPrimary_Throws()
    {
        var act = () => CatalogConfiguration.Read(Config((Key(0, "Id"), "secondary"), (Key(0, "Plugin"), "postgres")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'primary'*");
    }

    [Fact]
    public void Read_PrimaryMatchedIgnoringCase_IsAccepted()
    {
        var definitions = CatalogConfiguration.Read(Config((Key(0, "Id"), "Primary"), (Key(0, "Plugin"), "postgres")));

        definitions.Should().ContainSingle().Which.Id.Should().Be("Primary");
    }

    [Fact]
    public void Read_OnlyBlankEntries_ReturnsNoCatalogsAndNeedsNoPrimary()
    {
        var definitions = CatalogConfiguration.Read(Config((Key(0, "Id"), ""), (Key(0, "Plugin"), "")));

        definitions.Should().BeEmpty();
    }
}
