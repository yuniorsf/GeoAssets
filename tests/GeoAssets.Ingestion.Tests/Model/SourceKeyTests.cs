using System.Globalization;
using FluentAssertions;
using GeoAssets.Ingestion.Model;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Model;

public sealed class SourceKeyTests
{
    [Fact]
    public void ToString_JoinsProfileNamespaceAndNaturalKeyWithPipes()
    {
        new SourceKey("neoenergia-gse", "PG", "12345").ToString().Should().Be("neoenergia-gse|PG|12345");
    }

    [Fact]
    public void ForCoordinates_RoundsToTwoDecimalsInTheXyFormat()
    {
        var key = SourceKey.ForCoordinates("neoenergia-gse", "PG", 554271.85773680, 8566172.49068071);

        key.Profile.Should().Be("neoenergia-gse");
        key.Namespace.Should().Be("PG");
        key.NaturalKey.Should().Be("xy:554271.86:8566172.49");
    }

    [Fact]
    public void ForCoordinates_PadsToTwoDecimals()
    {
        SourceKey.ForCoordinates("p", "n", 10, 20.5).NaturalKey.Should().Be("xy:10.00:20.50");
    }

    [Fact]
    public void ForCoordinates_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            SourceKey.ForCoordinates("p", "n", 1.5, 2.25).NaturalKey.Should().Be("xy:1.50:2.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ForCoordinates_PointsWithinTheSameCentimetreShareAKey()
    {
        SourceKey.ForCoordinates("p", "n", 100.001, 200.002).Should().Be(SourceKey.ForCoordinates("p", "n", 100.004, 199.998));
    }

    [Fact]
    public void ForCoordinates_NegativeCoordinatesKeepTheirSign()
    {
        SourceKey.ForCoordinates("p", "n", -38.5, -12.97).NaturalKey.Should().Be("xy:-38.50:-12.97");
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        new SourceKey("a", "b", "c").Should().Be(new SourceKey("a", "b", "c"));
        new SourceKey("a", "b", "c").Should().NotBe(new SourceKey("a", "b", "d"));
    }
}
