using FluentAssertions;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>The dataset is the same on every machine and run, and has the make-up the baseline documents.</summary>
public sealed class DatasetTests
{
    [Fact]
    public void SplitMix64_MatchesTheReferenceSequence_SoTheDatasetDoesNotDependOnTheRuntime()
    {
        var rng = new SplitMix64(0);

        rng.NextUInt64().Should().Be(0xE220A8397B1DCDAFUL);
        rng.NextUInt64().Should().Be(0x6E789E6AA1B965F4UL);
        rng.NextUInt64().Should().Be(0x06C45D188009454FUL);
    }

    [Fact]
    public void SplitMix64_Doubles_StayInTheirRanges()
    {
        var rng = new SplitMix64(7);
        for (var i = 0; i < 10_000; i++)
        {
            rng.NextDouble().Should().BeInRange(0, 0.9999999999999999);
            rng.NextDouble(-2, 3).Should().BeInRange(-2, 3);
            rng.NextInt(5, 9).Should().BeInRange(5, 9);
        }
    }

    [Fact]
    public void Composition_SharesSumToOne_AndTheDefaultCountsAreTheDocumentedOnes()
    {
        DatasetComposition.AssetTypes.Sum(t => t.Share).Should().BeApproximately(1.0, 1e-12);

        DatasetComposition.Counts(DatasetComposition.DefaultFeatureCount).Select(c => (c.Type.Code, c.Count))
            .Should().Equal(("POSTE", 120_000), ("TRAFO", 20_000), ("REDE-MT", 15_000), ("REDE-BT", 25_000), ("LOTE", 15_000), ("SETOR", 5_000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1_001)]
    [InlineData(199_999)]
    public void Composition_CountsAlwaysAddUpToTheTotal(int total)
    {
        DatasetComposition.Counts(total).Sum(c => c.Count).Should().Be(total);
    }

    [Fact]
    public void Composition_EveryTypeHasAZoomRange_AndTheDistrictsAreAnOverviewOnlyLayer()
    {
        DatasetComposition.AssetTypes.Should().OnlyContain(t => t.MinZoom != null || t.MaxZoom != null);

        var setor = DatasetComposition.AssetTypes.Single(t => string.Equals(t.Code, "SETOR", StringComparison.Ordinal));
        setor.IsDrawnAt(8).Should().BeTrue();
        setor.IsDrawnAt(13).Should().BeTrue();
        setor.IsDrawnAt(14).Should().BeFalse();

        var lote = DatasetComposition.AssetTypes.Single(t => string.Equals(t.Code, "LOTE", StringComparison.Ordinal));
        lote.IsDrawnAt(15).Should().BeFalse();
        lote.IsDrawnAt(16).Should().BeTrue();
    }

    [Fact]
    public void Composition_Find_ResolvesTheTypesById_AndNothingElse()
    {
        var poste = DatasetComposition.AssetTypes[0];

        DatasetComposition.Find(poste.Id.ToString()).Should().BeSameAs(poste);
        DatasetComposition.Find(Guid.NewGuid().ToString()).Should().BeNull();
        DatasetComposition.Find("not-a-guid").Should().BeNull();
    }

    [Fact]
    public void Generator_SameSeed_GeneratesTheSameRows()
    {
        var first = new DatasetGenerator(DatasetGenerator.DefaultSeed).Generate(3_000).ToList();
        var second = new DatasetGenerator(DatasetGenerator.DefaultSeed).Generate(3_000).ToList();

        second.Select(Fingerprint).Should().Equal(first.Select(Fingerprint));
    }

    [Fact]
    public void Generator_AnotherSeed_GeneratesOtherGeometries()
    {
        var first = new DatasetGenerator(1).Generate(100).Select(r => r.Geometry.AsText());
        var second = new DatasetGenerator(2).Generate(100).Select(r => r.Geometry.AsText());

        second.Should().NotEqual(first);
    }

    [Fact]
    public void Generator_RowsMatchTheComposition_InterleavedAndValid()
    {
        const int total = 4_000;
        var rows = new DatasetGenerator(DatasetGenerator.DefaultSeed).Generate(total).ToList();

        rows.Should().HaveCount(total);
        rows.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.StartsWith(DatasetComposition.IdPrefix, StringComparison.Ordinal) && id.Length <= 36);
        rows.GroupBy(r => r.AssetType.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)
            .Should().BeEquivalentTo(DatasetComposition.Counts(total).ToDictionary(c => c.Type.Code, c => c.Count, StringComparer.Ordinal));
        rows.Take(50).Select(r => r.AssetType.Code).Distinct(StringComparer.Ordinal).Should().HaveCountGreaterThan(2, "types are interleaved, not grouped");

        foreach (var row in rows)
        {
            row.Geometry.SRID.Should().Be(4326);
            row.Geometry.IsValid.Should().BeTrue($"{row.Id} must be a valid {row.AssetType.Kind}");
            var expectedVertices = row.AssetType.Kind switch
            {
                BenchGeometryKind.Point => (1, 1),
                BenchGeometryKind.LineString => row.AssetType.VertexRange,
                _ => (row.AssetType.VertexRange.Min + 1, row.AssetType.VertexRange.Max + 1),
            };
            row.Geometry.NumPoints.Should().BeInRange(expectedVertices.Item1, expectedVertices.Item2);
            row.Geometry.Should().BeOfType(row.AssetType.Kind switch
            {
                BenchGeometryKind.Point => typeof(Point),
                BenchGeometryKind.LineString => typeof(LineString),
                _ => typeof(Polygon),
            });
        }
    }

    [Fact]
    public void Generator_PlacesTheFeaturesInTheConcession_DenserInTheTowns()
    {
        var rows = new DatasetGenerator(DatasetGenerator.DefaultSeed).Generate(5_000).ToList();
        var extent = DatasetLayout.Extent;

        // Positions are sampled inside the extent; a line (≤ 3 km) or a ring (radius ≤ 3 km) may reach a little past it.
        rows.Select(r => r.Geometry.EnvelopeInternal).Should().OnlyContain(e =>
            e.MinX >= extent.MinLon - 0.1 && e.MaxX <= extent.MaxLon + 0.1 && e.MinY >= extent.MinLat - 0.1 && e.MaxY <= extent.MaxLat + 0.1);
        rows.Where(r => r.AssetType.Kind == BenchGeometryKind.Point).Select(r => r.Geometry.Coordinate)
            .Should().OnlyContain(c => extent.Contains(c.X, c.Y));

        // The metro core (σ 0.12°) holds far more than its ~2% share of the area.
        var metro = rows.Count(r => Math.Abs(r.Geometry.Coordinate.X + 46.63) < 0.12 && Math.Abs(r.Geometry.Coordinate.Y + 23.55) < 0.12);
        metro.Should().BeGreaterThan(rows.Count / 10);
    }

    [Fact]
    public void Layout_IsDerivedFromTheSeed()
    {
        new DatasetLayout(5).Clusters.Should().Equal(new DatasetLayout(5).Clusters);
        new DatasetLayout(5).Clusters.Should().NotEqual(new DatasetLayout(6).Clusters);
        new DatasetLayout(5).Clusters.Should().HaveCount(DatasetLayout.ClusterCount);
    }

    [Fact]
    public void Seeder_RefusesADatabaseThatIsNotABenchmarkOne()
    {
        var act = () => DatasetSeeder.EnsureBenchDatabase("geoassets");

        act.Should().Throw<InvalidOperationException>().WithMessage("*must contain 'bench'*");
        DatasetSeeder.EnsureBenchDatabase("geoassets_render_bench");
    }

    private static string Fingerprint(BenchFeatureRow row) =>
        $"{row.Id}|{row.Name}|{row.AssetType.Code}|{row.Description}|{row.CreatedAt:O}|{row.UpdatedAt:O}|{row.Geometry.AsText()}|{row.CustomAttributesJson}";
}
