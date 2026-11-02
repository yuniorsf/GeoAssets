using System.Globalization;
using System.Text;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Minimal AutoCAD DXF R12 ASCII writer (XD01-284) — a <c>HEADER</c> section declaring
/// <c>$ACADVER AC1009</c> plus an <c>ENTITIES</c> section with one <c>POINT</c>/<c>TEXT</c> pair
/// per labeled point. No sample DXF or AutoCAD validation tool was available in this environment,
/// but DXF R12's ASCII group-code format (a group-code integer line followed by its value line,
/// repeated) is long-stable and extensively documented — this is the simplest subset of it that
/// still produces a file real DXF readers (AutoCAD, QGIS, ezdxf, etc.) accept, deliberately leaving
/// out TABLES/BLOCKS sections that aren't needed for plain point+label output.
/// </summary>
public static class DxfWriter
{
    public static byte[] WritePoints(IEnumerable<(double X, double Y, string Label)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var sb = new StringBuilder();
        WriteHeaderSection(sb);
        WriteEntitiesSection(sb, points);
        WritePair(sb, 0, "EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void WriteHeaderSection(StringBuilder sb)
    {
        WritePair(sb, 0, "SECTION");
        WritePair(sb, 2, "HEADER");
        WritePair(sb, 9, "$ACADVER");
        WritePair(sb, 1, "AC1009");
        WritePair(sb, 0, "ENDSEC");
    }

    private static void WriteEntitiesSection(StringBuilder sb, IEnumerable<(double X, double Y, string Label)> points)
    {
        WritePair(sb, 0, "SECTION");
        WritePair(sb, 2, "ENTITIES");

        foreach (var (x, y, label) in points)
        {
            WritePair(sb, 0, "POINT");
            WritePair(sb, 8, "0");
            WritePairD(sb, 10, x);
            WritePairD(sb, 20, y);
            WritePairD(sb, 30, 0.0);

            WritePair(sb, 0, "TEXT");
            WritePair(sb, 8, "0");
            WritePairD(sb, 10, x);
            WritePairD(sb, 20, y);
            WritePairD(sb, 30, 0.0);
            WritePairD(sb, 40, 1.0);
            WritePair(sb, 1, label);
        }

        WritePair(sb, 0, "ENDSEC");
    }

    private static void WritePair(StringBuilder sb, int groupCode, string value) =>
        sb.Append(groupCode.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(value).Append('\n');

    private static void WritePairD(StringBuilder sb, int groupCode, double value) =>
        WritePair(sb, groupCode, value.ToString("F6", CultureInfo.InvariantCulture));
}
