using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// The <c>pmtiles</c> provider plugin (XD01-438): connects an offline map pack — a <c>.pmtiles</c> archive of vector
/// tiles, or since XD01-465 of PNG, JPEG or WebP images — as a read-only pool entry the map draws with no connection: a
/// vector pack as features (<see cref="PMTilesPackAssetProvider"/>), a raster pack as a basemap tile layer
/// (<see cref="PMTilesRasterPackAssetProvider"/>), chosen by the pack's <see cref="PMTilesPack.Kind"/>.
/// <para>
/// Its <c>pack</c> field is a <see cref="ProviderFieldType.BinaryFile"/> accepting <c>.pmtiles</c> and
/// <see cref="ProviderConfigField.PathOnly"/>: <c>ProviderConfigForm</c> asks the native picker
/// (<see cref="INativeFilePicker.PickBinaryFileAsync"/>) for the file's path only, so a multi-gigabyte pack is never
/// read into memory. On connect, with the host's <see cref="PMTilesPackStorage"/>, the pack is imported (validated,
/// then streamed into the storage's folder) and <c>pack_path</c> is set to the stored copy — what the pool entry's
/// reconnect values and a later auto-boot reopen, offline, without a re-pick; without one it is read where it is. A vector
/// pack's tiles are read through the host's <see cref="ITileCache"/>, when it has one.
/// </para>
/// <para>
/// A pack larger than the storage's <see cref="PMTilesPackStorage.LargePackBytes"/> isn't copied on the first connect:
/// it answers the storage's size warning (how large it is, what importing it costs) and keeps the pack's id under
/// <see cref="ConfirmedLargePackKey"/>, so pressing Connect again with the same file imports it (XD01-465).
/// </para>
/// </summary>
public sealed class PMTilesPackProviderPlugin : IProviderPlugin
{
    /// <summary>The plugin's id (also its module's).</summary>
    public const string PluginId = "pmtiles";

    /// <summary>The key of the pack file field.</summary>
    public const string PackField = "pack";

    /// <summary>The configuration key of the pack's path.</summary>
    public const string PackPathKey = PackField + "_path";

    /// <summary>The configuration key of the id of the large pack the user was warned about, which the next connect imports.</summary>
    public const string ConfirmedLargePackKey = PackField + "_confirmed_large";

    public string Id => PluginId;

    public string DisplayName => "Offline map pack (PMTiles)";

    public string Description => "Show an offline map pack (.pmtiles) stored on this device — vector features or a basemap image — no connection needed";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label:        "Pack name",
            Placeholder:  "Offline map",
            DefaultValue: "Offline map"),
        new(PackField,
            Label:    "Offline map pack (.pmtiles)",
            Type:     ProviderFieldType.BinaryFile,
            Accept:   ".pmtiles",
            Required: true,
            PathOnly: true),
    ];

    /// <exception cref="InvalidOperationException">No pack was picked, the file is gone, or it is not an offline map pack this device can read.</exception>
    public async Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(services);

        var path = config.Get(PackPathKey);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Select an offline map pack (.pmtiles file) on this device.");

        var pack = await OpenAsync(path, config, services.GetService<PMTilesPackStorage>(), ct).ConfigureAwait(false);
        try
        {
            config.Set(PackPathKey, pack.Path);
            if (pack.Kind == PMTilesPackKind.Raster)
                return new PMTilesRasterPackAssetProvider(pack);

            var provider = new PMTilesPackAssetProvider(pack, services.GetService<ITileCache>());
            await provider.LoadOverviewAsync(ct).ConfigureAwait(false);
            return provider;
        }
        catch
        {
            pack.Dispose();
            throw;
        }
    }

    private static async Task<PMTilesPack> OpenAsync(string path, ProviderConfig config, PMTilesPackStorage? storage, CancellationToken ct)
    {
        try
        {
            if (storage is not null)
                return await storage.ImportAsync(path, config.Get(ConfirmedLargePackKey), ct).ConfigureAwait(false);
            return await PMTilesPack.OpenAsync(path, ct).ConfigureAwait(false);
        }
        catch (PMTilesPackSizeConfirmationException ex)
        {
            config.Set(ConfirmedLargePackKey, ex.PackId);
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"The offline map pack '{Path.GetFileName(path)}' is no longer on this device. Please select it again.", ex);
        }
        catch (PMTilesFormatException ex)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(path)}' can't be used as an offline map pack: {ex.Message}", ex);
        }
    }
}
