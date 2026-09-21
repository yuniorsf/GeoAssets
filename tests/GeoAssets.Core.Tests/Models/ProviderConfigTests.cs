using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class ProviderConfigTests
{
    // ── ToReconnectValues ─────────────────────────────────────────────────────

    [Fact]
    public void ToReconnectValues_NoSensitiveFields_ReturnsAllValues()
    {
        var config = new ProviderConfig();
        config.Set("url", "https://example.test");
        config.Set("name", "My Layer");

        var result = config.ToReconnectValues([
            new ProviderConfigField("url", "URL", ProviderFieldType.Url),
            new ProviderConfigField("name", "Name"),
        ]);

        result.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["url"]  = "https://example.test",
            ["name"] = "My Layer",
        });
    }

    [Fact]
    public void ToReconnectValues_StripsPasswordFieldKeys()
    {
        // XD01-157: ProjectProviderEntry.Values must never carry credentials — the user
        // re-enters them on reconnect.
        var config = new ProviderConfig();
        config.Set("url", "https://example.test");
        config.Set("apiKey", "super-secret");

        var result = config.ToReconnectValues([
            new ProviderConfigField("url", "URL", ProviderFieldType.Url),
            new ProviderConfigField("apiKey", "API Key", ProviderFieldType.Password),
        ]);

        result.Should().ContainKey("url");
        result.Should().NotContainKey("apiKey");
    }

    [Fact]
    public void ToReconnectValues_StripsContentSuffixedKeys()
    {
        var config = new ProviderConfig();
        config.Set("file", "layer.geojson");
        config.Set("file_content", "{ \"type\": \"FeatureCollection\" }");

        var result = config.ToReconnectValues([
            new ProviderConfigField("file", "File", ProviderFieldType.File),
        ]);

        result.Should().ContainKey("file");
        result.Should().NotContainKey("file_content");
    }

    [Fact]
    public void ToReconnectValues_UnknownKeyNotDeclaredAsAField_IsKept()
    {
        // Only *_content and declared Password keys are stripped — anything else (e.g. "name",
        // which every plugin's form adds ad hoc, undeclared) passes through untouched.
        var config = new ProviderConfig();
        config.Set("name", "My Layer");

        var result = config.ToReconnectValues([]);

        result.Should().ContainKey("name").WhoseValue.Should().Be("My Layer");
    }

    [Fact]
    public void ToReconnectValues_ReturnsAnIndependentDictionary()
    {
        var config = new ProviderConfig();
        config.Set("url", "https://example.test");

        var result = config.ToReconnectValues([]);
        config.Set("url", "mutated");

        result["url"].Should().Be("https://example.test");
    }
}
