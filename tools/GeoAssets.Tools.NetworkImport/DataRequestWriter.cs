using System.Globalization;
using System.Text;
using GeoAssets.Ingestion.Profiles.NeoenergiaGse;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// The Markdown data request sent back to the distributor: per region and layer, the files missing and the layers
/// that are geometry-only or unusable (with the inventory's findings); the layers a region lacks; and the code tables
/// the profile cannot decode.
/// </summary>
internal static class DataRequestWriter
{
    public static string Render(DeliveryInventory inventory, IReadOnlyList<string> layerNames)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Data request — {inventory.ProfileId} delivery").AppendLine().AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"Delivery folder: `{inventory.RootPath}`").AppendLine().AppendLine();

        text.AppendLine("## Incomplete layers").AppendLine();
        var incomplete = inventory.Layers.Where(layer => layer.Verdict != LayerVerdict.Usable || MissingFiles(layer).Count > 0).ToList();
        if (incomplete.Count == 0)
        {
            text.AppendLine("None: every layer delivered is complete and usable.").AppendLine();
        }
        else
        {
            text.AppendLine("| Region | Layer | Verdict | Missing files | Findings |").AppendLine("|---|---|---|---|---|");
            foreach (var layer in incomplete)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $"| {RegionLabel(layer.Region)} | {layer.Layer} | {layer.Verdict} | {Cell(string.Join(", ", MissingFiles(layer)))} | {Cell(Findings(inventory, layer))} |")
                    .AppendLine();
            }

            text.AppendLine();
        }

        text.AppendLine("## Layers not delivered").AppendLine();
        var absent = inventory.Layers
            .GroupBy(layer => layer.Region, StringComparer.Ordinal)
            .Select(region => (Region: region.Key, Missing: layerNames.Where(name => !region.Any(layer => string.Equals(layer.Layer, name, StringComparison.OrdinalIgnoreCase))).ToList()))
            .Where(region => region.Missing.Count > 0)
            .ToList();
        if (absent.Count == 0)
            text.AppendLine("None: every region holds every layer.");
        foreach (var (region, missing) in absent)
            text.Append(CultureInfo.InvariantCulture, $"- {RegionLabel(region)}: {string.Join(", ", missing)}").AppendLine();
        text.AppendLine();

        text.AppendLine("## Code tables needed").AppendLine();
        var tables = CodeTables(inventory.ProfileId);
        if (tables.Count == 0)
            text.AppendLine("None.");
        foreach (var table in tables)
            text.Append(CultureInfo.InvariantCulture, $"- `{table}`").AppendLine();
        return text.ToString();
    }

    private static List<string> MissingFiles(LayerFileSet layer)
    {
        var missing = new List<string>();
        if (layer.GeometryPath is null)
            missing.Add(".shp");
        if (layer.IndexPath is null)
            missing.Add(".shx");
        if (layer.AttributeTablePath is null)
            missing.Add(".dbf");
        if (layer.CrsWkt is null)
            missing.Add(".prj");
        return missing;
    }

    private static string Findings(DeliveryInventory inventory, LayerFileSet layer)
    {
        var prefix = layer.Region.Length == 0 ? string.Empty : layer.Region + "/";
        var subjects = new[] { layer.Layer + ".shp", layer.Layer + ".dbf", Path.GetFileName(layer.GeometryPath), Path.GetFileName(layer.AttributeTablePath) }
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => prefix + name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Join("; ", inventory.Issues
            .Where(issue => subjects.Contains(issue.Subject))
            .Select(issue => $"{issue.Code}: {issue.Message}")
            .Distinct(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> CodeTables(string profileId) =>
        string.Equals(profileId, new NeoenergiaGseProfile().Id, StringComparison.OrdinalIgnoreCase) ? NeoenergiaGseProfile.UndecodedCodeTables : [];

    private static string RegionLabel(string region) => region.Length == 0 ? "(root)" : region;

    private static string Cell(string value) => value.Length == 0 ? "–" : value.Replace("|", "\\|", StringComparison.Ordinal);
}
