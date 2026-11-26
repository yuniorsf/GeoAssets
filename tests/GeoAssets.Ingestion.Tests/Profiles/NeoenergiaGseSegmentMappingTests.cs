using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class NeoenergiaGseSegmentMappingTests
{
    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find("neoenergia-gse");

    private static CanonicalElement MapMv(
        long id = 9, long? from = 100, long? to = 200, long? fromNode = 1, long? toNode = 2, long? installation = null, long? protection = null)
    {
        var record = ProfileRecords.Record(
            "REDE_MT", TestData.Line((10, 20), (30, 40)),
            ("NU_TRECHO_MT_ID", id), ("NU_PG_INICIAL_ID", from), ("NU_PG_FINAL_ID", to),
            ("NU_NO_INICIAL", fromNode), ("NU_NO_FINAL", toNode),
            ("NU_INSTALACAO_MT_ID", installation), ("NU_CHAVE_MT_ID", protection));
        return ProfileRecords.Map(Profile, record).Element!;
    }

    [Fact]
    public void RedeMt_KeyKindClassAndName()
    {
        var element = MapMv(id: 9);

        element.Key.Should().Be(new SourceKey("neoenergia-gse", "TRECHO_MT", "9"));
        element.Kind.Should().Be(ElementKind.Segment);
        element.AssetClass.Should().Be("MvSegment");
        element.Name.Should().Be("MT 9");
        element.Scope.Should().Be(ProfileRecords.Scope);
    }

    [Fact]
    public void RedeMt_StartAndEndReferenceThePolesWithNodesAndSnapPoints()
    {
        var element = MapMv();

        var start = element.References.Single(r => r.Role == ReferenceRole.Start);
        start.TargetNamespace.Should().Be("PG");
        start.TargetKey.Should().Be("100");
        start.LocalNode.Should().Be(1);
        (start.SnapPoint!.X, start.SnapPoint.Y).Should().Be((10, 20));

        var end = element.References.Single(r => r.Role == ReferenceRole.End);
        end.TargetNamespace.Should().Be("PG");
        end.TargetKey.Should().Be("200");
        end.LocalNode.Should().Be(2);
        (end.SnapPoint!.X, end.SnapPoint.Y).Should().Be((30, 40));
    }

    [Fact]
    public void RedeMt_SamePoleAndSameNodeAtBothEnds_IsAJumper()
    {
        MapMv(from: 100, to: 100, fromNode: 1, toNode: 1).AssetClass.Should().Be("MvJumper");
    }

    [Fact]
    public void RedeMt_SamePoleButDifferentNodes_IsAnOrdinarySegment()
    {
        MapMv(from: 100, to: 100, fromNode: 1, toNode: 2).AssetClass.Should().Be("MvSegment");
    }

    [Fact]
    public void RedeMt_DifferentPolesWithTheSameNode_IsAnOrdinarySegment()
    {
        MapMv(from: 100, to: 200, fromNode: 1, toNode: 1).AssetClass.Should().Be("MvSegment");
    }

    [Fact]
    public void RedeMt_InstallationAndProtectionGroupsBecomeMemberOfReferencesWithQualifiers()
    {
        var element = MapMv(installation: 55, protection: 66);

        var members = element.References.Where(r => r.Role == ReferenceRole.MemberOf).ToArray();
        members.Should().HaveCount(2);
        var feeder = members.Single(r => string.Equals(r.TargetNamespace, "INST", StringComparison.Ordinal));
        feeder.TargetKey.Should().Be("55");
        feeder.Qualifier.Should().Be("feeder-head");
        var zone = members.Single(r => string.Equals(r.TargetNamespace, "CHAVE", StringComparison.Ordinal));
        zone.TargetKey.Should().Be("66");
        zone.Qualifier.Should().Be("protection-zone");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void RedeMt_ZeroOrMissingGroupIds_ProduceNoMemberOfReference(long? group)
    {
        MapMv(installation: group, protection: group).References.Should().NotContain(r => r.Role == ReferenceRole.MemberOf);
    }

    [Fact]
    public void RedeBt_IsAnLvSegmentFedByTheInstallationWhenPresent()
    {
        var record = ProfileRecords.Record(
            "REDE_BT", TestData.Line((0, 0), (5, 5)),
            ("NU_TRECHO_BT_ID", 5L), ("NU_PG_INICIAL_ID", 1L), ("NU_PG_FINAL_ID", 2L), ("NU_NO_INICIAL", 1L), ("NU_NO_FINAL", 2L),
            ("NU_INSTALACAO_MT_ID", 88L));

        var element = ProfileRecords.Map(Profile, record).Element!;

        element.Key.Should().Be(new SourceKey("neoenergia-gse", "TRECHO_BT", "5"));
        element.AssetClass.Should().Be("LvSegment");
        element.Name.Should().Be("BT 5");
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End, ReferenceRole.FedBy]);
        var fedBy = element.References.Single(r => r.Role == ReferenceRole.FedBy);
        fedBy.TargetNamespace.Should().Be("INST");
        fedBy.TargetKey.Should().Be("88");
    }

    [Fact]
    public void RedeBt_WithoutAnInstallation_HasNoFedByReference()
    {
        var record = ProfileRecords.Record(
            "REDE_BT", TestData.Line((0, 0), (5, 5)),
            ("NU_TRECHO_BT_ID", 5L), ("NU_PG_INICIAL_ID", 1L), ("NU_PG_FINAL_ID", 2L), ("NU_INSTALACAO_MT_ID", 0L));

        ProfileRecords.Map(Profile, record).Element!.References.Should().NotContain(r => r.Role == ReferenceRole.FedBy);
    }

    [Fact]
    public void RedeLt_IsAnHvSegmentWithStartAndEndOnly()
    {
        var record = ProfileRecords.Record(
            "REDE_LT", TestData.Line((0, 0), (5, 5)),
            ("NU_TRECHO_LT_ID", 3L), ("NU_PG_INICIAL_ID", 1L), ("NU_PG_FINAL_ID", 2L), ("NU_NO_INICIAL", 1L), ("NU_NO_FINAL", 1L));

        var element = ProfileRecords.Map(Profile, record).Element!;

        element.Key.Should().Be(new SourceKey("neoenergia-gse", "TRECHO_LT", "3"));
        element.AssetClass.Should().Be("HvSegment");
        element.Name.Should().Be("LT 3");
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
    }
}
