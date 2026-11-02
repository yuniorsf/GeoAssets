using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Enel JoiN-AS export (XD01-284's AC2): a CSV/XML manifest with safety-distance verification
/// fields. Same PONNOT/<c>COD_ID</c> selection as <see cref="DisNor056Exporter"/>, independently —
/// this exporter never references that one, satisfying AC3 (a third distributor needs no changes
/// to either).
/// </summary>
public sealed class JoinAsExporter : IDossierExporter
{
    private const string CodIdAttributeKey = "COD_ID";

    public string DistributorName => "Enel";

    public DossierResult Export(IReadOnlyList<GeoFeature> poleFeatures)
    {
        var poles = SelectLabeledPoles(poleFeatures);

        return new DossierResult([
            new DossierFile("join-as-manifest.csv", Encoding.UTF8.GetBytes(BuildCsv(poles))),
            new DossierFile("join-as-manifest.xml", Encoding.UTF8.GetBytes(BuildXml(poles))),
        ]);
    }

    private static List<GeoFeature> SelectLabeledPoles(IReadOnlyList<GeoFeature> poleFeatures)
    {
        ArgumentNullException.ThrowIfNull(poleFeatures);
        var ponnotId = BdgdAssetTypeIds.Ponnot.ToString();
        var poles = poleFeatures
            .Where(f => string.Equals(f.Properties.AssetTypeId, ponnotId, StringComparison.Ordinal))
            .Where(f => f.Geometry is GeoPoint && f.Properties.CustomAttributes.ContainsKey(CodIdAttributeKey))
            .ToList();

        if (poles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No PONNOT pole features with a '{CodIdAttributeKey}' attribute were found to export.");
        }

        return poles;
    }

    private static string BuildCsv(IReadOnlyList<GeoFeature> poles)
    {
        var sb = new StringBuilder();
        sb.Append("CodId,Longitude,Latitude,SecondarySafetyDistanceMeters,MediumVoltageSafetyDistanceMeters\n");
        foreach (var pole in poles)
        {
            var point = (GeoPoint)pole.Geometry!;
            var codId = EscapeCsv(pole.Properties.CustomAttributes[CodIdAttributeKey]);
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{codId},{point.Longitude:F6},{point.Latitude:F6},{PoleSafetyDistances.SecondaryMeters:F2},{PoleSafetyDistances.MediumVoltageMeters:F2}"));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value) => value.Contains(',', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string BuildXml(IReadOnlyList<GeoFeature> poles)
    {
        var document = new XDocument(new XElement("JoinAsManifest", poles.Select(BuildPoleElement)));
        return document.ToString();
    }

    private static XElement BuildPoleElement(GeoFeature pole)
    {
        var point = (GeoPoint)pole.Geometry!;
        return new XElement("Pole",
            new XElement("CodId", pole.Properties.CustomAttributes[CodIdAttributeKey]),
            new XElement("Longitude", point.Longitude.ToString("F6", CultureInfo.InvariantCulture)),
            new XElement("Latitude", point.Latitude.ToString("F6", CultureInfo.InvariantCulture)),
            new XElement("SecondarySafetyDistanceMeters", PoleSafetyDistances.SecondaryMeters.ToString("F2", CultureInfo.InvariantCulture)),
            new XElement("MediumVoltageSafetyDistanceMeters", PoleSafetyDistances.MediumVoltageMeters.ToString("F2", CultureInfo.InvariantCulture)));
    }
}
