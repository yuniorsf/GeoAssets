using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Inventories a Shapefile delivery: every directory under the root (recursively) holding files is a region, named by
/// its path relative to the root with <c>/</c> separators (<c>""</c> for the root). Within a region, a layer's file
/// set is the <c>.shp/.shx/.dbf/.prj/.cpg</c> files whose stem is the layer's <see cref="LayerDefinition.FileStem"/>
/// (case-insensitive). Each set is checked (sidecars, orphan <c>.dbf</c> pairing, duplicates across regions, geometry
/// sanity) and gets a <see cref="LayerVerdict"/>; findings become <see cref="ImportIssue"/>s.
/// </summary>
public sealed class ShapefileDeliveryInventory : ISourceInventory
{
    public async Task<DeliveryInventory> InspectAsync(string rootPath, ISourceProfile profile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(profile);

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The delivery folder '{rootPath}' does not exist.");

        var regions = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Prepend(root)
            .Select(directory => (Directory: directory, Region: RegionName(root, directory)))
            .OrderBy(entry => entry.Region, StringComparer.Ordinal);

        var layers = new List<LayerFileSet>();
        var issues = new List<ImportIssue>();
        var firstRegionByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (directory, region) in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = Directory.GetFiles(directory);
            if (files.Length == 0)
                continue;

            var inspection = new RegionInspection(region, files, profile, firstRegionByHash, issues);
            layers.AddRange(await inspection.InspectAsync(cancellationToken).ConfigureAwait(false));
        }

        return new DeliveryInventory(root, profile.Id, layers, issues);
    }

    private static string RegionName(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        return string.Equals(relative, ".", StringComparison.Ordinal)
            ? string.Empty
            : relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
