using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Components.OfflinePacks;

/// <summary>
/// The offline maps on this device (XD01-464): every pack the host's <see cref="IOfflineMapPackCatalog"/> stores — its
/// name, what it holds, its size, when it was added, its zooms and area, and which collections draw it — with the
/// storage they use together, and a delete behind <see cref="GeoAssets.Shared.Components.Shared.ConfirmDialog"/>.
/// Deleting takes the collections that draw the pack off the map (their <see cref="IProviderMapLayerRenderer"/>, as the
/// Collections panel removes an entry) before the catalog removes them, the file and its cached tiles. A pack the active
/// collection draws can't be deleted until another collection is active. Reloads when the provider pool changes. For a
/// host that stores packs (MAUI); it needs the catalog registered.
/// </summary>
public partial class OfflinePacksPanel
{
    private OfflineMapPackListing? _listing;
    private OfflineMapPack? _pendingDelete;
    private string _message = string.Empty;
    private MessageTone _tone;
    private bool _busy;

    // How a status message reads.
    private enum MessageTone
    {
        Success,
        Warning,
        Error,
    }

    [Inject] private IOfflineMapPackCatalog Catalog { get; set; } = null!;

    [Inject] private IProviderPool Pool { get; set; } = null!;

    [Inject] private ProviderMapLayerRendererChain Renderers { get; set; } = null!;

    [Inject] private ICurrentMapContext MapContext { get; set; } = null!;

    [Inject] private ILogger<OfflinePacksPanel> Logger { get; set; } = null!;

    private string ConfirmMessage => _pendingDelete is { } pack
        ? L.GetString("offlinePacks.confirmMessage", pack.DisplayName, SizeText(pack.SizeBytes))
        : string.Empty;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Pool.Changed += OnPoolChanged;
    }

    protected override Task OnInitializedAsync() => LoadAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Pool.Changed -= OnPoolChanged;
        base.Dispose(disposing);
    }

    /// <summary><paramref name="bytes"/> for a person, in <paramref name="culture"/>'s digits: <c>0 B</c>, <c>512 KB</c>, <c>3.4 MB</c>, <c>1.2 GB</c> (1 KB = 1,024 bytes).</summary>
    public static string FormatSize(long bytes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        const double kibibyte = 1024d;
        const double mebibyte = kibibyte * 1024d;
        const double gibibyte = mebibyte * 1024d;
        return bytes switch
        {
            >= (long)gibibyte => (bytes / gibibyte).ToString("0.#", culture) + " GB",
            >= (long)mebibyte => (bytes / mebibyte).ToString("0.#", culture) + " MB",
            >= (long)kibibyte => Math.Round(bytes / kibibyte).ToString("0", culture) + " KB",
            _ => Math.Max(0, bytes).ToString(culture) + " B",
        };
    }

    /// <summary>The area <paramref name="pack"/> covers, south-west to north-east as latitude, longitude; <see langword="null"/> when unknown.</summary>
    public static string? FormatArea(OfflineMapPack pack, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(culture);
        if (pack.Bounds is not { } bounds)
            return null;

        return string.Create(culture, $"{bounds.MinLat:0.###}, {bounds.MinLon:0.###} – {bounds.MaxLat:0.###}, {bounds.MaxLon:0.###}");
    }

    private static string SizeText(long bytes) => FormatSize(bytes, CultureInfo.CurrentCulture);

    private static string FormatDate(DateTimeOffset at) => at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static bool IsDrawnByActiveEntry(OfflineMapPack pack) => pack.UsedBy.Any(use => use.IsActive);

    private static string ToneClass(MessageTone tone) => tone switch
    {
        MessageTone.Success => "success",
        MessageTone.Warning => "warning",
        _ => "error",
    };

    private string KindLabel(OfflineMapPackKind kind) => kind switch
    {
        OfflineMapPackKind.Vector => L["offlinePacks.kind.vector"],
        OfflineMapPackKind.Raster => L["offlinePacks.kind.raster"],
        _ => L["offlinePacks.kind.unreadable"],
    };

    private string ZoomText(OfflineMapPack pack) => pack is { MinZoom: { } min, MaxZoom: { } max }
        ? string.Create(CultureInfo.CurrentCulture, $"{min}–{max}")
        : L["offlinePacks.unknown"];

    private string AreaText(OfflineMapPack pack) => FormatArea(pack, CultureInfo.CurrentCulture) ?? L["offlinePacks.unknown"];

    private string UsedByText(OfflineMapPack pack) => pack.UsedBy.Count == 0
        ? L["offlinePacks.notOnMap"]
        : string.Join(", ", pack.UsedBy.Select(use => use.Name));

    private void OnPoolChanged(object? sender, EventArgs e) => _ = InvokeAsync(LoadAsync);

    private async Task LoadAsync()
    {
        try
        {
            _listing = await Catalog.ListAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _listing ??= new OfflineMapPackListing([], 0);
            Show(L.GetString("offlinePacks.loadError", ex.Message), MessageTone.Error);
            Logger.LogError(ex, "Listing the offline map packs failed");
        }

        StateHasChanged();
    }

    private void AskDelete(OfflineMapPack pack)
    {
        _message = string.Empty;
        _pendingDelete = pack;
    }

    private void CancelDelete() => _pendingDelete = null;

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDelete is not { } pack)
            return;

        _pendingDelete = null;
        _busy = true;
        try
        {
            var result = await Catalog.DeleteAsync(pack.Id, RemoveFromMapAsync, CancellationToken.None);
            switch (result)
            {
                case OfflineMapPackDeletion.Deleted:
                    Show(L.GetString("offlinePacks.deleted", pack.DisplayName, SizeText(pack.SizeBytes)), MessageTone.Success);
                    Logger.LogInformation("Offline map pack deleted — id: {PackId}, bytes: {Bytes}", pack.Id, pack.SizeBytes);
                    break;
                case OfflineMapPackDeletion.InUseByActiveEntry:
                    Show(L.GetString("offlinePacks.inUseByActive", pack.DisplayName), MessageTone.Warning);
                    break;
                default:
                    Show(L.GetString("offlinePacks.alreadyGone", pack.DisplayName), MessageTone.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            Show(L.GetString("offlinePacks.deleteError", pack.DisplayName, ex.Message), MessageTone.Error);
            Logger.LogError(ex, "Deleting offline map pack {PackId} failed", pack.Id);
        }
        finally
        {
            _busy = false;
        }

        await LoadAsync();
    }

    private async Task RemoveFromMapAsync(ProviderEntry entry)
    {
        if (Renderers.Resolve(entry.Provider) is { } renderer)
            await renderer.RemoveAsync(MapLayerRenderRequest.For(MapContext.MapDivId, entry));
    }

    private void Show(string message, MessageTone tone)
    {
        _message = message;
        _tone = tone;
    }
}
