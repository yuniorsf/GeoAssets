using System.Globalization;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Models;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Components.OfflinePacks;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Rendering;
using GeoAssets.Shared.Tests.Services.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.OfflinePacks;

/// <summary>
/// XD01-464 — <see cref="OfflinePacksPanel"/>: the stored offline maps with their metadata and the storage they use, and
/// a delete behind the confirmation dialog that takes the collections drawing the pack off the map; a pack the active
/// collection draws can't be deleted.
/// </summary>
public sealed class OfflinePacksPanelTests : BunitContext
{
    private const string Div = "geoassets-map";

    private static readonly DateTimeOffset Imported = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private readonly ProviderPool _pool = new();
    private readonly LayerRecordingMapInterop _map = new();
    private readonly FakeCatalog _catalog = new();

    private IRenderedComponent<OfflinePacksPanel> RenderPanel()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new ArgsLocalizer());
        Services.AddSingleton<IOfflineMapPackCatalog>(_catalog);
        Services.AddSingleton<IProviderPool>(_pool);
        Services.AddSingleton<IMapInterop>(_map);
        Services.AddSingleton<ICurrentMapContext>(new MapContext());
        Services.AddSingleton<FeatureRenderPipeline>();
        Services.AddProviderMapLayerRenderers();
        Services.AddSingleton<ILogger<OfflinePacksPanel>>(NullLogger<OfflinePacksPanel>.Instance);
        return Render<OfflinePacksPanel>();
    }

    private static OfflineMapPack Pack(
        string id, string name, OfflineMapPackKind kind = OfflineMapPackKind.Vector, long size = 3_670_016,
        params OfflineMapPackUse[] usedBy) =>
        new(id, name, name + ".pmtiles", kind, size, Imported, new BoundingBox(-38.6, -13.1, -38.3, -12.8), 2, 14, usedBy);

    [Fact]
    public void EveryPack_IsListedWithItsMetadata_AndTheTotalStorage()
    {
        var entry = _pool.Add("North grid", new FakeLayerProvider());
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid", usedBy: new OfflineMapPackUse(entry.Id, "North grid", false)));
        _catalog.Packs.Add(Pack("fedcba9876543210", "basemap", OfflineMapPackKind.Raster, 1_288_490_189) with { Bounds = null, MinZoom = null, MaxZoom = null });

        var panel = RenderPanel();

        var rows = panel.FindAll(".offline-pack");
        rows.Select(r => r.GetAttribute("data-pack-id")).Should().Equal("0123456789abcdef", "fedcba9876543210");
        rows[0].QuerySelector(".offline-pack-name")!.TextContent.Should().Be("North grid");
        rows[0].QuerySelector(".offline-pack-kind")!.TextContent.Should().Be("offlinePacks.kind.vector");
        rows[0].QuerySelector(".offline-pack-size")!.TextContent.Should().Be(OfflinePacksPanel.FormatSize(3_670_016, CultureInfo.CurrentCulture));
        rows[0].QuerySelector(".offline-pack-added")!.TextContent.Should().Be(Imported.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        rows[0].QuerySelector(".offline-pack-zoom")!.TextContent.Should().Be("2–14");
        rows[0].QuerySelector(".offline-pack-area")!.TextContent.Should().Be(OfflinePacksPanel.FormatArea(_catalog.Packs[0], CultureInfo.CurrentCulture));
        rows[0].QuerySelector(".offline-pack-used-by")!.TextContent.Should().Be("North grid");

        rows[1].QuerySelector(".offline-pack-kind")!.TextContent.Should().Be("offlinePacks.kind.raster");
        rows[1].QuerySelector(".offline-pack-zoom")!.TextContent.Should().Be("offlinePacks.unknown");
        rows[1].QuerySelector(".offline-pack-area")!.TextContent.Should().Be("offlinePacks.unknown");
        rows[1].QuerySelector(".offline-pack-used-by")!.TextContent.Should().Be("offlinePacks.notOnMap");

        var total = OfflinePacksPanel.FormatSize(3_670_016 + 1_288_490_189, CultureInfo.CurrentCulture);
        panel.Find(".offline-packs-total").TextContent.Should().Be($"offlinePacks.totalUsed|{total}");
    }

    [Fact]
    public void NoPacks_ShowsHowToAddOne()
    {
        var panel = RenderPanel();

        panel.FindAll(".offline-pack").Should().BeEmpty();
        panel.Find(".offline-packs-empty").TextContent.Should().Be("offlinePacks.empty");
        panel.Find(".offline-packs-total").TextContent.Should().Be($"offlinePacks.totalUsed|{OfflinePacksPanel.FormatSize(0, CultureInfo.CurrentCulture)}");
    }

    [Fact]
    public void Deleting_AsksFirst_ThenTakesTheCollectionOffTheMap_AndReloads()
    {
        var features = new[] { new GeoFeature { Id = "f-0" }, new GeoFeature { Id = "f-1" } };
        var entry = _pool.Add("North grid", new FakeLayerProvider(features));
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid", usedBy: new OfflineMapPackUse(entry.Id, "North grid", false)));
        _catalog.Drawing["0123456789abcdef"] = [entry];
        var panel = RenderPanel();

        panel.Find(".offline-pack-delete").Click();

        _catalog.Deleted.Should().BeEmpty("nothing is deleted before the user confirms");
        panel.Find(".dialog-box h6").TextContent.Should().Be("offlinePacks.confirmTitle");
        panel.Find(".dialog-box p").TextContent.Should().Be(
            $"offlinePacks.confirmMessage|North grid|{OfflinePacksPanel.FormatSize(3_670_016, CultureInfo.CurrentCulture)}");

        panel.Find(".dialog-box .btn-danger").Click();

        panel.WaitForAssertion(() => _catalog.Deleted.Should().Equal("0123456789abcdef"));
        _map.RemovedFeatures.Should().Equal((Div, "f-0"), (Div, "f-1"));
        panel.WaitForAssertion(() => panel.FindAll(".offline-pack").Should().BeEmpty());
        panel.FindAll(".dialog-box").Should().BeEmpty();
        panel.Find(".offline-packs-msg--success").TextContent.Should().StartWith("offlinePacks.deleted|North grid|");
    }

    [Fact]
    public void Cancelling_DeletesNothing()
    {
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid"));
        var panel = RenderPanel();

        panel.Find(".offline-pack-delete").Click();
        panel.Find(".dialog-box .btn-secondary").Click();

        panel.FindAll(".dialog-box").Should().BeEmpty();
        _catalog.Deleted.Should().BeEmpty();
        panel.FindAll(".offline-pack").Should().ContainSingle();
    }

    [Fact]
    public void APackTheActiveCollectionDraws_CannotBeDeleted_AndSaysWhy()
    {
        var active = _pool.Add("North grid", new FakeLayerProvider());
        _pool.SetActive(active.Id);
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid", usedBy: new OfflineMapPackUse(active.Id, "North grid", true)));

        var panel = RenderPanel();

        panel.Find(".offline-pack-delete").HasAttribute("disabled").Should().BeTrue();
        panel.Find(".offline-pack-active-hint").TextContent.Should().Be("offlinePacks.activeHint");
    }

    [Fact]
    public void ARefusedOrFailedDelete_IsReported()
    {
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid"));
        _catalog.Packs.Add(Pack("fedcba9876543210", "South grid"));
        _catalog.Results["0123456789abcdef"] = OfflineMapPackDeletion.InUseByActiveEntry;
        _catalog.Failures["fedcba9876543210"] = new IOException("disk busy");
        var panel = RenderPanel();

        panel.FindAll(".offline-pack-delete")[0].Click();
        panel.Find(".dialog-box .btn-danger").Click();
        panel.WaitForAssertion(() => panel.Find(".offline-packs-msg--warning").TextContent.Should().Be("offlinePacks.inUseByActive|North grid"));

        panel.FindAll(".offline-pack-delete")[1].Click();
        panel.Find(".dialog-box .btn-danger").Click();
        panel.WaitForAssertion(() => panel.Find(".offline-packs-msg--error").TextContent.Should().Be("offlinePacks.deleteError|South grid|disk busy"));
        panel.FindAll(".offline-pack").Should().HaveCount(2);
    }

    [Fact]
    public void APackAlreadyGone_IsReported_AndAListingFailureToo()
    {
        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid"));
        _catalog.Results["0123456789abcdef"] = OfflineMapPackDeletion.NotFound;
        var panel = RenderPanel();

        panel.Find(".offline-pack-delete").Click();
        panel.Find(".dialog-box .btn-danger").Click();
        panel.WaitForAssertion(() => panel.Find(".offline-packs-msg--warning").TextContent.Should().Be("offlinePacks.alreadyGone|North grid"));

        _catalog.ListFailure = new IOException("storage unavailable");
        panel.Find(".offline-packs-refresh").Click();
        panel.WaitForAssertion(() => panel.Find(".offline-packs-msg--error").TextContent.Should().Be("offlinePacks.loadError|storage unavailable"));
    }

    [Fact]
    public void AChangeToThePool_ReloadsTheList()
    {
        var panel = RenderPanel();
        var lists = _catalog.ListCalls;

        _catalog.Packs.Add(Pack("0123456789abcdef", "North grid"));
        _pool.Add("North grid", new FakeLayerProvider());

        panel.WaitForAssertion(() => panel.FindAll(".offline-pack").Should().ContainSingle());
        _catalog.ListCalls.Should().BeGreaterThan(lists);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3_670_016, "3.5 MB")]
    [InlineData(1_288_490_189, "1.2 GB")]
    public void Sizes_ReadForAPerson(long bytes, string expected)
    {
        OfflinePacksPanel.FormatSize(bytes, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Fact]
    public void TheArea_ReadsSouthWestToNorthEast_LatitudeFirst()
    {
        OfflinePacksPanel.FormatArea(Pack("0123456789abcdef", "a"), CultureInfo.InvariantCulture).Should().Be("-13.1, -38.6 – -12.8, -38.3");
        OfflinePacksPanel.FormatArea(Pack("0123456789abcdef", "a") with { Bounds = null }, CultureInfo.InvariantCulture).Should().BeNull();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void ThePanelsKeys_AreTranslated(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var markup = File.ReadAllText(Path.Combine(directory.FullName, "apps", "GeoAssets.Shared", "Components", "OfflinePacks", "OfflinePacksPanel.razor"))
                     + File.ReadAllText(Path.Combine(directory.FullName, "apps", "GeoAssets.Shared", "Components", "OfflinePacks", "OfflinePacksPanel.razor.cs"));
        var used = System.Text.RegularExpressions.Regex.Matches(markup, "\"(?<key>offlinePacks\\.[A-Za-z.]+)\"", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups["key"].Value).Distinct(StringComparer.Ordinal).ToArray();

        used.Should().NotBeEmpty();
        foreach (var key in used)
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }

    private sealed class MapContext : ICurrentMapContext
    {
        public string MapDivId => Div;
    }

    /// <summary>Answers a key with itself, and its arguments after it: <c>key|arg|arg</c>.</summary>
    private sealed class ArgsLocalizer : IJsonStringLocalizer
    {
        public string this[string key] => key;

        public string GetString(string key, params object[] args) =>
            args.Length == 0 ? key : key + "|" + string.Join("|", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));

        public event EventHandler LocalizationChanged { add { } remove { } }
    }

    private sealed class FakeCatalog : IOfflineMapPackCatalog
    {
        public List<OfflineMapPack> Packs { get; } = [];

        public Dictionary<string, ProviderEntry[]> Drawing { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, OfflineMapPackDeletion> Results { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        public List<string> Deleted { get; } = [];

        public Exception? ListFailure { get; set; }

        public int ListCalls { get; private set; }

        public Task<OfflineMapPackListing> ListAsync(CancellationToken ct)
        {
            ListCalls++;
            return ListFailure is { } failure
                ? Task.FromException<OfflineMapPackListing>(failure)
                : Task.FromResult(new OfflineMapPackListing([.. Packs], Packs.Sum(p => p.SizeBytes)));
        }

        public async Task<OfflineMapPackDeletion> DeleteAsync(string packId, Func<ProviderEntry, Task> removeFromMap, CancellationToken ct)
        {
            if (Failures.TryGetValue(packId, out var failure))
                throw failure;
            if (Results.TryGetValue(packId, out var result))
                return result;

            foreach (var entry in Drawing.GetValueOrDefault(packId, []))
                await removeFromMap(entry);
            Deleted.Add(packId);
            Packs.RemoveAll(p => string.Equals(p.Id, packId, StringComparison.Ordinal));
            return OfflineMapPackDeletion.Deleted;
        }
    }
}
