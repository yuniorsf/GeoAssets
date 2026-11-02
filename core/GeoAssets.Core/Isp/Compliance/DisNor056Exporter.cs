using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Neoenergia DIS-NOR-056 export (XD01-284's AC1): a DXF/Shapefile pair in SIRGAS 2000 UTM, pole
/// labels taken from the ingested BDGD <c>PONNOT</c> layer's <c>COD_ID</c> attribute (XD01-224).
/// Only features whose <see cref="GeoFeatureProperties.AssetTypeId"/> is
/// <see cref="BdgdAssetTypeIds.Ponnot"/> and that carry a <c>COD_ID"</c> attribute participate —
/// a pole without one has nothing to label it with in this export. Depends only on
/// <see cref="IShapefileExportService"/> (the interface Core already owns), never the concrete
/// provider, so Onion layering stays intact.
/// </summary>
public sealed class DisNor056Exporter(IShapefileExportService shapefileExportService) : IConcessionaireDossierExporter
{
    private const string CodIdAttributeKey = "COD_ID";

    public string DistributorName => "Neoenergia";

    public ConcessionaireDossierResult Export(IReadOnlyList<GeoFeature> poleFeatures)
    {
        var poles = SelectLabeledPoles(poleFeatures);
        var firstLongitude = ((GeoPoint)poles[0].Geometry!).Longitude;
        var zone = SirgasUtmZoneResolver.ZoneFor(firstLongitude);
        var epsgCode = SirgasUtmZoneResolver.EpsgCodeFor(firstLongitude);

        var dxfPoints = new List<(double X, double Y, string Label)>(poles.Count);
        var reprojectedFeatures = new List<GeoFeature>(poles.Count);
        foreach (var pole in poles)
        {
            var point = (GeoPoint)pole.Geometry!;
            var label = pole.Properties.CustomAttributes[CodIdAttributeKey];
            var (easting, northing, _) = SirgasUtmReprojector.ToSirgasUtm(point);

            dxfPoints.Add((easting, northing, label));
            reprojectedFeatures.Add(new GeoFeature
            {
                Id = pole.Id,
                Geometry = new GeoPoint(easting, northing, epsgCode),
                Properties = pole.Properties,
            });
        }

        var dxfBytes = DxfWriter.WritePoints(dxfPoints);
        var shapefileZip = shapefileExportService.ExportToZip(reprojectedFeatures, SirgasUtmReprojector.WktFor(zone), epsgCode);

        return new ConcessionaireDossierResult([
            new ConcessionaireDossierFile("dis-nor-056-poles.dxf", dxfBytes),
            new ConcessionaireDossierFile("dis-nor-056-poles-shapefile.zip", shapefileZip),
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
}
