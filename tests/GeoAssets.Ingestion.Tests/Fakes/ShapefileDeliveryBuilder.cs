using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class ShapefileDeliveryBuilder : IDisposable
{
    public const string Sad69Utm24SWkt =
        "PROJCS[\"SAD_1969_UTM_Zone_24S\",GEOGCS[\"GCS_South_American_1969\",DATUM[\"D_South_American_1969\",SPHEROID[\"GRS_1967_Truncated\",6378160.0,298.25]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0],PARAMETER[\"Central_Meridian\",-39.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";

    private static readonly GeometryFactory Factory = new();

    public ShapefileDeliveryBuilder()
    {
        Root = Path.Combine(Path.GetTempPath(), "network-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public static IFeature PointFeature(double x, double y, params (string Name, object Value)[] attributes) =>
        new Feature(Factory.CreatePoint(new Coordinate(x, y)), Table(attributes));

    public static IFeature LineFeature(IReadOnlyList<(double X, double Y)> points, params (string Name, object Value)[] attributes) =>
        new Feature(Factory.CreateLineString([.. points.Select(p => new Coordinate(p.X, p.Y))]), Table(attributes));

    public string PathOf(string region, string file) =>
        region.Length == 0 ? Path.Combine(Root, file) : Path.Combine(Root, region.Replace('/', Path.DirectorySeparatorChar), file);

    public string WriteLayer(
        string region,
        string stem,
        IReadOnlyList<IFeature> features,
        bool dbf = true,
        bool prj = true,
        string? cpg = "UTF-8",
        string? lineageFieldMap = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        var directory = Path.GetDirectoryName(PathOf(region, stem))!;
        Directory.CreateDirectory(directory);
        var basePath = PathOf(region, stem);
        var encoding = cpg is null ? Encoding.Latin1 : Encoding.GetEncoding(cpg);
        var header = BuildHeader(features[0], features.Count, encoding);
        var writer = new ShapefileDataWriter(basePath, Factory, encoding)
        {
            Header = header,
        };
        writer.Write(features.ToList());

        if (!dbf)
        {
            File.Delete(basePath + ".dbf");
        }

        if (prj)
        {
            File.WriteAllText(basePath + ".prj", Sad69Utm24SWkt);
        }

        if (cpg is not null)
        {
            File.WriteAllText(basePath + ".cpg", cpg);
        }

        if (lineageFieldMap is not null)
        {
            File.WriteAllText(basePath + ".shp.xml", Lineage(stem, lineageFieldMap));
        }

        return basePath;
    }

    public string WriteOrphanTable(string region, string name, IReadOnlyList<IFeature> features)
    {
        var basePath = WriteLayer(region, name, features, cpg: "UTF-8");
        File.Delete(basePath + ".shp");
        File.Delete(basePath + ".shx");
        File.Delete(basePath + ".prj");
        File.Delete(basePath + ".cpg");
        return basePath + ".dbf";
    }

    public void CopyGeometry(string sourceRegion, string stem, string targetRegion)
    {
        var source = PathOf(sourceRegion, stem);
        var target = PathOf(targetRegion, stem);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        foreach (var extension in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
        {
            if (File.Exists(source + extension))
            {
                File.Copy(source + extension, target + extension, overwrite: true);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static DbaseFileHeader BuildHeader(IFeature first, int count, Encoding encoding)
    {
        var header = new DbaseFileHeader(encoding);
        foreach (var name in first.Attributes.GetNames())
        {
            switch (first.Attributes[name])
            {
                case long or int:
                    header.AddColumn(name, 'N', 18, 0);
                    break;
                case double:
                    header.AddColumn(name, 'N', 18, 8);
                    break;
                case DateTime:
                    header.AddColumn(name, 'D', 8, 0);
                    break;
                default:
                    header.AddColumn(name, 'C', 254, 0);
                    break;
            }
        }

        header.NumRecords = count;
        return header;
    }

    private static AttributesTable Table(IEnumerable<(string Name, object Value)> attributes)
    {
        var table = new AttributesTable(StringComparer.Ordinal);
        foreach (var (name, value) in attributes)
        {
            table.Add(name, value);
        }

        return table;
    }

    private static string Lineage(string stem, string fieldMap) =>
        "<?xml version=\"1.0\"?><metadata xml:lang=\"en\"><Esri><DataProperties><lineage>"
        + "<Process ToolSource=\"c:\\arcgis\\Toolbox\\FeatureClassToFeatureClass.tbx\" Date=\"20240101\" Time=\"101010\">"
        + $"FeatureClassToFeatureClass C:\\source.gdb\\{stem} C:\\out {stem}.shp # \"{fieldMap}\" #</Process>"
        + "<Process ToolSource=\"c:\\arcgis\\Toolbox\\Other.tbx\" Date=\"20240102\" Time=\"101010\">Other tool # \"IGNORED \"Ignored\"\" #</Process>"
        + "</lineage></DataProperties></Esri></metadata>";
}
