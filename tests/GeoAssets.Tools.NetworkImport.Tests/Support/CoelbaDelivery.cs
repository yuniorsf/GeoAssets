using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Text;

namespace GeoAssets.Tools.NetworkImport.Tests.Support;

internal sealed class CoelbaDelivery : IDisposable
{
    private const string Sad69Utm24SWkt =
        "PROJCS[\"SAD_1969_UTM_Zone_24S\",GEOGCS[\"GCS_South_American_1969\",DATUM[\"D_South_American_1969\",SPHEROID[\"GRS_1967_Truncated\",6378160.0,298.25]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0],PARAMETER[\"Central_Meridian\",-39.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";

    private const double X0 = 554271.85;
    private const double Y0 = 8566172.49;

    private static readonly GeometryFactory Factory = new();

    private readonly TempDirectory _directory = new();

    public CoelbaDelivery()
    {
        Write("", "POSTE",
        [
            Point(X0, Y0, ("NU_PG_ID", 1L), ("CD_TIPO_PG", 1L)),
            Point(X0 + 20, Y0, ("NU_PG_ID", 2L), ("CD_TIPO_PG", 1L)),
            Point(X0 + 40, Y0, ("NU_PG_ID", 3L), ("CD_TIPO_PG", 90L)),
        ]);
        Write("", "INSTALACAO",
        [
            Point(X0, Y0, ("NU_INSTALA", 10L), ("DE_SIGLA_1", "DJ"), ("NU_PG_ID", 1L), ("NU_NO", 1L)),
        ]);
        Write("", "REDE_MT",
        [
            Line([(X0, Y0), (X0 + 20, Y0)], ("NU_TRECHO_", 100L), ("NU_PG_INIC", 1L), ("NU_PG_FINA", 2L), ("NU_NO_INIC", 1L), ("NU_NO_FINA", 1L)),
        ]);
    }

    public string Root => _directory.Path;

    public void AddGeometryOnlyLowVoltage()
    {
        Write("", "REDE_BT", [Line([(X0, Y0 + 5), (X0 + 20, Y0 + 5)], ("NU_TRECHO_", 200L))], writeDbf: false);
    }

    public void AddRegion(string region, int poleCount)
    {
        Write(region, "POSTE", [.. Enumerable.Range(1, poleCount).Select(i => Point(X0 + 1000 + i, Y0 + 1000, ("NU_PG_ID", 500L + i), ("CD_TIPO_PG", 1L)))]);
    }

    public void Dispose() => _directory.Dispose();

    private static Feature Point(double x, double y, params (string Name, object Value)[] attributes) =>
        new(Factory.CreatePoint(new Coordinate(x, y)), Table(attributes));

    private static Feature Line(IReadOnlyList<(double X, double Y)> points, params (string Name, object Value)[] attributes) =>
        new(Factory.CreateLineString([.. points.Select(p => new Coordinate(p.X, p.Y))]), Table(attributes));

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

    private void Write(string region, string stem, IReadOnlyList<IFeature> features, bool writeDbf = true)
    {
        var directory = region.Length == 0 ? Root : System.IO.Path.Combine(Root, region);
        Directory.CreateDirectory(directory);
        var basePath = System.IO.Path.Combine(directory, stem);
        var encoding = Encoding.UTF8;
        var writer = new ShapefileDataWriter(basePath, Factory, encoding)
        {
            Header = BuildHeader(features[0], features.Count, encoding),
        };
        writer.Write(features.ToList());
        if (!writeDbf)
        {
            File.Delete(basePath + ".dbf");
        }

        File.WriteAllText(basePath + ".prj", Sad69Utm24SWkt);
        File.WriteAllText(basePath + ".cpg", "UTF-8");
    }
}
