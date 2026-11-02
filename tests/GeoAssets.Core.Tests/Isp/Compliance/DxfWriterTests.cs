using System.Globalization;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Isp.Compliance;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance;

/// <summary>
/// Covers <see cref="DxfWriter.WritePoints"/> by parsing the written ASCII bytes back into their
/// group-code/value line pairs (DXF's own format: a group-code integer line followed by its value
/// line, repeated) and asserting the HEADER/ENTITIES structure rather than matching the raw bytes
/// verbatim, since byte-for-byte matching would just restate the production code.
/// </summary>
public class DxfWriterTests
{
    private static List<(int Code, string Value)> ParsePairs(byte[] dxfBytes)
    {
        var lines = Encoding.ASCII.GetString(dxfBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var pairs = new List<(int Code, string Value)>();
        for (var i = 0; i < lines.Length; i += 2)
            pairs.Add((int.Parse(lines[i], CultureInfo.InvariantCulture), lines[i + 1]));
        return pairs;
    }

    [Fact]
    public void WritePoints_SinglePoint_ProducesHeaderAndOneEntityPair()
    {
        var bytes = DxfWriter.WritePoints([(123.456, 789.012, "POLE-1")]);
        var pairs = ParsePairs(bytes);

        pairs[0].Should().Be((0, "SECTION"));
        pairs[1].Should().Be((2, "HEADER"));
        pairs[2].Should().Be((9, "$ACADVER"));
        pairs[3].Should().Be((1, "AC1009"));
        pairs[4].Should().Be((0, "ENDSEC"));

        pairs[5].Should().Be((0, "SECTION"));
        pairs[6].Should().Be((2, "ENTITIES"));

        pairs[7].Should().Be((0, "POINT"));
        pairs[8].Should().Be((8, "0"));
        pairs[9].Should().Be((10, 123.456.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[10].Should().Be((20, 789.012.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[11].Should().Be((30, 0.0.ToString("F6", CultureInfo.InvariantCulture)));

        pairs[12].Should().Be((0, "TEXT"));
        pairs[13].Should().Be((8, "0"));
        pairs[14].Should().Be((10, 123.456.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[15].Should().Be((20, 789.012.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[16].Should().Be((30, 0.0.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[17].Should().Be((40, 1.0.ToString("F6", CultureInfo.InvariantCulture)));
        pairs[18].Should().Be((1, "POLE-1"));

        pairs[19].Should().Be((0, "ENDSEC"));
        pairs[^1].Should().Be((0, "EOF"));
    }

    [Fact]
    public void WritePoints_MultiplePoints_ProducesOneEntityPairPerPointInOrder()
    {
        var points = new[] { (1.0, 2.0, "A"), (3.0, 4.0, "B"), (5.0, 6.0, "C") };

        var pairs = ParsePairs(DxfWriter.WritePoints(points));

        var textLabels = pairs.Where(p => p.Code == 1 && !string.Equals(p.Value, "AC1009", StringComparison.Ordinal)).Select(p => p.Value).ToList();
        textLabels.Should().Equal("A", "B", "C");

        var pointEntityCount = pairs.Count(p => p == (0, "POINT"));
        var textEntityCount = pairs.Count(p => p == (0, "TEXT"));
        pointEntityCount.Should().Be(3);
        textEntityCount.Should().Be(3);
    }

    [Fact]
    public void WritePoints_EmptyInput_ProducesValidEmptyDxfWithNoEntities()
    {
        var pairs = ParsePairs(DxfWriter.WritePoints([]));

        pairs.Should().Equal(
            (0, "SECTION"), (2, "HEADER"), (9, "$ACADVER"), (1, "AC1009"), (0, "ENDSEC"),
            (0, "SECTION"), (2, "ENTITIES"), (0, "ENDSEC"),
            (0, "EOF"));
    }

    [Fact]
    public void WritePoints_NullInput_ThrowsArgumentNullException()
    {
        var act = () => DxfWriter.WritePoints(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WritePoints_Always_StartsWithSectionAndEndsWithEof()
    {
        var pairs = ParsePairs(DxfWriter.WritePoints([(1, 2, "L")]));

        pairs[0].Should().Be((0, "SECTION"));
        pairs[^1].Should().Be((0, "EOF"));
    }
}
