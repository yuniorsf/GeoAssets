using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Sources;

/// <summary>Everything the inventory found in a delivery folder, plus the issues (missing files, corrupt layers…).</summary>
public sealed record DeliveryInventory(
    string RootPath,
    string ProfileId,
    IReadOnlyList<LayerFileSet> Layers,
    IReadOnlyList<ImportIssue> Issues);
