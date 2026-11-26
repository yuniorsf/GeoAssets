using System.Globalization;
using GeoAssets.Ingestion.Shapefile;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Tools.NetworkImport;

/// <summary><c>inventory</c>: inspects the delivery, writes <c>inventory.json</c> and <c>data-request.md</c>, prints a summary.</summary>
internal static class InventoryCommand
{
    public const string InventoryFile = "inventory.json";
    public const string DataRequestFile = "data-request.md";

    public static async Task<int> RunAsync(NetworkImportOptions options, TextWriter stdout, CancellationToken cancellationToken)
    {
        var profile = NetworkImportInputs.Profile(options.Profile);
        var root = NetworkImportInputs.Root(options.Root);
        var inventory = await new ShapefileDeliveryInventory().InspectAsync(root, profile, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(options.OutputDirectory);
        var inventoryPath = Path.Combine(options.OutputDirectory, InventoryFile);
        var requestPath = Path.Combine(options.OutputDirectory, DataRequestFile);
        await NetworkImportJson.WriteFileAsync(inventoryPath, inventory, cancellationToken).ConfigureAwait(false);
        var layerNames = profile.Layers.Select(layer => layer.Name).ToList();
        await File.WriteAllTextAsync(requestPath, DataRequestWriter.Render(inventory, layerNames), cancellationToken).ConfigureAwait(false);

        await stdout.LineAsync(Summary(inventory, inventoryPath, requestPath), cancellationToken).ConfigureAwait(false);
        return NetworkImportExitCodes.Succeeded;
    }

    private static string Summary(DeliveryInventory inventory, string inventoryPath, string requestPath)
    {
        int Count(LayerVerdict verdict) => inventory.Layers.Count(layer => layer.Verdict == verdict);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Inventory: {inventory.Layers.Count} layer file sets in {inventory.Layers.Select(layer => layer.Region).Distinct(StringComparer.Ordinal).Count()} regions " +
            $"({Count(LayerVerdict.Usable)} usable, {Count(LayerVerdict.GeometryOnly)} geometry-only, {Count(LayerVerdict.Unusable)} unusable), " +
            $"{inventory.Issues.Count} issues. Wrote {inventoryPath} and {requestPath}.");
    }
}
