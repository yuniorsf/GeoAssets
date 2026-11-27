using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Components.Providers;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Providers;

/// <summary>
/// XD01-423 — a <see cref="ProviderFieldType.BinaryFile"/> field declares the file types it accepts through
/// <see cref="ProviderConfigField.Accept"/>: the native picker (MAUI) is asked for exactly those extensions and
/// the browser file input carries exactly that filter, with no <c>.zip</c> assumed for a field that declares none.
/// </summary>
public sealed class ProviderConfigFormBinaryFileTests : BunitContext
{
    private readonly RecordingFilePicker _picker = new();

    public ProviderConfigFormBinaryFileTests() =>
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());

    private IRenderedComponent<ProviderConfigForm> RenderForm(ProviderConfigField field, ProviderConfig config, bool withNativePicker)
    {
        if (withNativePicker) Services.AddSingleton<INativeFilePicker>(_picker);
        return Render<ProviderConfigForm>(p => p
            .Add(f => f.Plugin, new FieldPlugin(field))
            .Add(f => f.Config, config));
    }

    private static ProviderConfigField BinaryField(string? accept) =>
        new("archive", "Archive", ProviderFieldType.BinaryFile, Accept: accept);

    [Fact]
    public async Task TheNativePicker_IsAskedForTheFieldsDeclaredExtensions()
    {
        var form = RenderForm(BinaryField(".pmtiles, mbtiles"), new ProviderConfig(), withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        _picker.Calls.Should().ContainSingle().Which.Should().Equal(".pmtiles", ".mbtiles");
    }

    [Fact]
    public async Task TheNativePicker_IsAskedForAnyFile_WhenTheFieldDeclaresNoExtension()
    {
        var form = RenderForm(BinaryField(null), new ProviderConfig(), withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        _picker.Calls.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task APickedFile_IsStoredWithItsContentNameAndPath()
    {
        var config = new ProviderConfig();
        _picker.Result = new NativeFilePickResult([1, 2, 3], "tiles.pmtiles", "/data/tiles.pmtiles");
        var form = RenderForm(BinaryField(".pmtiles"), config, withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        config.Get("archive_content").Should().Be(Convert.ToBase64String([1, 2, 3]));
        config.Get("archive_name").Should().Be("tiles.pmtiles");
        config.Get("archive_path").Should().Be("/data/tiles.pmtiles");
        form.Find(".pcf-file-name").TextContent.Should().Be("tiles.pmtiles");
    }

    [Fact]
    public async Task AFieldWithItsContent_IsPickedForItsBytes()
    {
        var form = RenderForm(BinaryField(".zip"), new ProviderConfig(), withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        _picker.Contents.Should().Equal(NativeFilePickContent.Bytes);
    }

    [Fact]
    public async Task APathOnlyField_IsPickedForItsPathOnly_AndStoresNoContent()
    {
        var config = new ProviderConfig();
        _picker.Result = new NativeFilePickResult([], "city.pmtiles", "/tmp/Inbox/city.pmtiles");
        var form = RenderForm(
            new ProviderConfigField("pack", "Pack", ProviderFieldType.BinaryFile, Accept: ".pmtiles", PathOnly: true),
            config,
            withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        _picker.Calls.Should().ContainSingle().Which.Should().Equal(".pmtiles");
        _picker.Contents.Should().Equal(NativeFilePickContent.PathOnly);
        config.Has("pack_content").Should().BeFalse("a multi-gigabyte pack is never read into the configuration");
        config.Get("pack_path").Should().Be("/tmp/Inbox/city.pmtiles");
        config.Get("pack_name").Should().Be("city.pmtiles");
    }

    [Fact]
    public async Task ACancelledPick_StoresNothing()
    {
        var config = new ProviderConfig();
        var form = RenderForm(BinaryField(".fgb"), config, withNativePicker: true);

        await form.Find("button.pcf-file").ClickAsync(new());

        config.All.Should().BeEmpty();
        form.FindAll(".pcf-file-name").Should().BeEmpty();
    }

    [Fact]
    public void TheBrowserFileInput_CarriesTheFieldsAcceptFilter()
    {
        var form = RenderForm(BinaryField(".parquet"), new ProviderConfig(), withNativePicker: false);

        form.FindAll("button.pcf-file").Should().BeEmpty();
        form.Find("input[type=file]").GetAttribute("accept").Should().Be(".parquet");
    }

    [Fact]
    public void TheBrowserFileInput_AcceptsAnyFile_WhenTheFieldDeclaresNoFilter()
    {
        var form = RenderForm(BinaryField(null), new ProviderConfig(), withNativePicker: false);

        form.Find("input[type=file]").HasAttribute("accept").Should().BeFalse();
    }

    [Fact]
    public void ATextFileField_KeepsItsGeoJsonDefaultFilter()
    {
        var form = RenderForm(new ProviderConfigField("geojson", "GeoJSON", ProviderFieldType.File), new ProviderConfig(), withNativePicker: true);

        form.Find("input[type=file]").GetAttribute("accept").Should().Be(".geojson,.json");
    }

    private sealed class RecordingFilePicker : INativeFilePicker
    {
        public List<IReadOnlyList<string>?> Calls { get; } = [];
        public List<NativeFilePickContent> Contents { get; } = [];
        public NativeFilePickResult? Result { get; set; }

        public Task<NativeFilePickResult?> PickBinaryFileAsync(
            IReadOnlyList<string>? acceptedExtensions = null,
            NativeFilePickContent content = NativeFilePickContent.Bytes,
            CancellationToken ct = default)
        {
            Calls.Add(acceptedExtensions);
            Contents.Add(content);
            return Task.FromResult(Result);
        }
    }

    private sealed class FieldPlugin(ProviderConfigField field) : IProviderPlugin
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public string Description => "Test plugin";
        public IReadOnlyList<ProviderConfigField> ConfigFields { get; } = [field];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
