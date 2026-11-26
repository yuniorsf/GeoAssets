using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class EquatorialQgisMunicipalSegmentMappingTests
{
    private const string ProfileId = "equatorial-qgis-municipal";

    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find(ProfileId);

    private static CanonicalElement MapMv(Geometry geometry, long? mslink = 4711L) =>
        ProfileRecords.Map(
            Profile,
            ProfileRecords.Record(
                "TRECHO_PRIMARIO", geometry,
                ("MUN_ID", 1L), ("MUN_NOME", "PACO DO LUMIAR"), ("MSLINK", mslink), ("X1", 10.0), ("Y1", 20.0), ("X2", 30.0), ("Y2", 40.0))).Element!;

    private static CanonicalElement MapLv(Geometry geometry, long? mslink, object? msLinkIns) =>
        ProfileRecords.Map(
            Profile,
            ProfileRecords.Record(
                "TRECHO_SECUNDARIO", geometry,
                ("MUN_ID", 1L), ("MUN_NOME", "PACO DO LUMIAR"), ("MSLINK", mslink), ("MSLINK_INS", msLinkIns),
                ("X1", 10.0), ("Y1", 20.0), ("X2", 30.0), ("Y2", 40.0))).Element!;

    private static (double X, double Y) Xy(ElementReference reference) => (reference.SnapPoint!.X, reference.SnapPoint.Y);

    [Fact]
    public void Mv_KeyKindClassNameAndScope()
    {
        var element = MapMv(TestData.Line((10, 20), (30, 40)), 4711L);

        element.Key.Should().Be(new SourceKey(ProfileId, "TRECHO_MT", "4711"));
        element.Kind.Should().Be(ElementKind.Segment);
        element.AssetClass.Should().Be("MvSegment");
        element.Name.Should().Be("MT 4711");
        element.Scope.Should().Be(ProfileRecords.Scope);
        element.Completeness.Should().Be(Completeness.Full);
    }

    [Fact]
    public void Mv_StartAndEndSnapToPolesFromTheFirstAndLastCoordinateWithoutOffset()
    {
        var element = MapMv(TestData.Line((10.5, 20.5), (15, 25), (30.25, 40.75)));

        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
        element.References.Should().OnlyContain(r => string.Equals(r.TargetNamespace, "PG", StringComparison.Ordinal) && r.TargetKey == null && r.GroupKey == null);
        Xy(element.References.Single(r => r.Role == ReferenceRole.Start)).Should().Be((10.5, 20.5));
        Xy(element.References.Single(r => r.Role == ReferenceRole.End)).Should().Be((30.25, 40.75));
    }

    [Fact]
    public void Mv_NeverDeclaresAFedByReference()
    {
        MapMv(TestData.Line((0, 0), (5, 5))).References.Should().NotContain(r => r.Role == ReferenceRole.FedBy);
    }

    [Fact]
    public void Mv_StartEqualToEndAtCentimetrePrecision_IsAJumper()
    {
        var element = MapMv(TestData.Line((10, 20), (10.004, 20.003)));

        element.AssetClass.Should().Be("MvJumper");
        element.Kind.Should().Be(ElementKind.Segment);
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
    }

    [Fact]
    public void Mv_ClosedLine_IsAJumper()
    {
        MapMv(TestData.Line((10, 20), (15, 25), (10, 20))).AssetClass.Should().Be("MvJumper");
    }

    [Fact]
    public void Mv_EndsMoreThanACentimetreApart_IsAnOrdinarySegment()
    {
        MapMv(TestData.Line((10, 20), (10.02, 20))).AssetClass.Should().Be("MvSegment");
    }

    [Fact]
    public void Mv_WithoutMslink_IsKeyedByTheLineCentroid()
    {
        var element = MapMv(TestData.Line((0, 0), (10, 0)), mslink: null);

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "TRECHO_MT", 5, 0));
    }

    [Fact]
    public void Mv_GeometryOnlyRecord_IsKeyedByCentroidWithSnapOnlyEndsAndNoGroup()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("TRECHO_PRIMARIO", TestData.Line((0, 0), (10, 0)))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "TRECHO_MT", 5, 0));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("MvSegment");
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
        element.References.Should().OnlyContain(r => r.TargetKey == null && string.Equals(r.TargetNamespace, "PG", StringComparison.Ordinal) && r.SnapPoint != null);
    }

    [Fact]
    public void Lv_KeyKindClassNameAndScope()
    {
        var element = MapLv(TestData.Line((10, 20), (30, 40)), 99L, 5L);

        element.Key.Should().Be(new SourceKey(ProfileId, "TRECHO_BT", "99"));
        element.Kind.Should().Be(ElementKind.Segment);
        element.AssetClass.Should().Be("LvSegment");
        element.Name.Should().Be("BT 99");
        element.Scope.Should().Be(ProfileRecords.Scope);
    }

    [Fact]
    public void Lv_StartAndEndSnapPointsAreTheEndCoordinatesMinusTheDrawingOffset()
    {
        var element = MapLv(TestData.Line((100.5, 200.5), (150, 250), (110, 220)), 99L, 5L);

        var start = element.References.Single(r => r.Role == ReferenceRole.Start);
        var end = element.References.Single(r => r.Role == ReferenceRole.End);
        start.TargetNamespace.Should().Be("PG");
        start.TargetKey.Should().BeNull();
        end.TargetNamespace.Should().Be("PG");
        end.TargetKey.Should().BeNull();
        Xy(start).Should().Be((99.5, 199.5));
        Xy(end).Should().Be((109, 219));
    }

    [Fact]
    public void Lv_OffsetMatchesTheLayerDefinition()
    {
        var layer = Profile.Layers.Single(l => string.Equals(l.Name, "TRECHO_SECUNDARIO", StringComparison.Ordinal));
        var element = MapLv(TestData.Line((100, 200), (110, 220)), 99L, 5L);

        Xy(element.References.Single(r => r.Role == ReferenceRole.Start)).Should().Be((100 - layer.DrawingOffsetX, 200 - layer.DrawingOffsetY));
    }

    [Fact]
    public void Lv_FedByGroupKeyIsTheMslinkInsAsAnInvariantIntegerString()
    {
        var element = MapLv(TestData.Line((0, 0), (5, 5)), 99L, msLinkIns: 1234567L);

        var fedBy = element.References.Single(r => r.Role == ReferenceRole.FedBy);
        fedBy.TargetNamespace.Should().Be("TRAFO");
        fedBy.TargetKey.Should().BeNull();
        fedBy.GroupKey.Should().Be("1234567");
        fedBy.SnapPoint.Should().BeNull();
    }

    [Fact]
    public void Lv_FedByGroupKeyFromADoubleHasNoFractionalPart()
    {
        MapLv(TestData.Line((0, 0), (5, 5)), 99L, msLinkIns: 77.0).References.Single(r => r.Role == ReferenceRole.FedBy).GroupKey.Should().Be("77");
    }

    [Fact]
    public void Lv_ElementsOfTheSameTransformerShareTheGroupKey()
    {
        var first = MapLv(TestData.Line((0, 0), (5, 5)), 1L, 42L);
        var second = MapLv(TestData.Line((5, 5), (9, 9)), 2L, 42L);

        first.References.Single(r => r.Role == ReferenceRole.FedBy).GroupKey
            .Should().Be(second.References.Single(r => r.Role == ReferenceRole.FedBy).GroupKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(0.0)]
    public void Lv_WhenMslinkInsIsZeroOrMissing_HasNoGroupAndOnlyStartAndEnd(object? msLinkIns)
    {
        var element = MapLv(TestData.Line((0, 0), (5, 5)), 99L, msLinkIns: msLinkIns);

        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
        element.References.Should().OnlyContain(r => r.GroupKey == null);
    }

    [Fact]
    public void Lv_WithoutAnMslinkInsField_HasNoGroup()
    {
        var record = ProfileRecords.Record("TRECHO_SECUNDARIO", TestData.Line((0, 0), (5, 5)), ("MSLINK", 3L));

        ProfileRecords.Map(Profile, record).Element!.References.Should().NotContain(r => r.Role == ReferenceRole.FedBy);
    }

    [Fact]
    public void Lv_GeometryOnlyRecord_IsKeyedByCentroidWithOffsetSnapEndsAndNoGroup()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("TRECHO_SECUNDARIO", TestData.Line((0, 0), (10, 0)))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "TRECHO_BT", 5, 0));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("LvSegment");
        element.Attributes.Should().BeEmpty();
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
        element.References.Should().OnlyContain(r => r.GroupKey == null && r.TargetKey == null);
        Xy(element.References.Single(r => r.Role == ReferenceRole.Start)).Should().Be((-1, -1));
        Xy(element.References.Single(r => r.Role == ReferenceRole.End)).Should().Be((9, -1));
    }
}
