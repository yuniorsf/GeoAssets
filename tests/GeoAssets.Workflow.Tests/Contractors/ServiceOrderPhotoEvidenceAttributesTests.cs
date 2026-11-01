using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

public class ServiceOrderPhotoEvidenceAttributesTests
{
    private static Dictionary<string, string> Attributes(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }

        return dict;
    }

    [Fact]
    public void ReadEvidenceStages_Missing_ReturnsEmpty()
    {
        ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(Attributes()).Should().BeEmpty();
    }

    [Fact]
    public void ReadEvidenceStages_SingleStage_ParsesItemCodeStageAndHash()
    {
        var attributes = Attributes((ServiceOrderPhotoEvidenceAttributes.EvidenceStagesKey,
            """[{"itemCode":"US-EL-014","stage":"Before","evidenceHash":"c2lnbmF0dXJl"}]"""));

        var stages = ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(attributes);

        stages.Should().ContainSingle();
        stages[0].ItemCode.Should().Be("US-EL-014");
        stages[0].Stage.Should().Be(PhotoGatingStage.Before);
        stages[0].EvidenceHash.Should().Be("c2lnbmF0dXJl");
    }

    [Fact]
    public void ReadEvidenceStages_MultipleStages_ParsesAllInOrder()
    {
        var attributes = Attributes((ServiceOrderPhotoEvidenceAttributes.EvidenceStagesKey,
            """[{"itemCode":"US-EL-014","stage":"Before","evidenceHash":"a"},{"itemCode":"US-EL-014","stage":"During","evidenceHash":"b"}]"""));

        var stages = ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(attributes);

        stages.Should().HaveCount(2);
        stages[0].Stage.Should().Be(PhotoGatingStage.Before);
        stages[1].Stage.Should().Be(PhotoGatingStage.During);
    }

    [Fact]
    public void ReadEvidenceStages_MalformedJson_Throws()
    {
        var attributes = Attributes((ServiceOrderPhotoEvidenceAttributes.EvidenceStagesKey, "not json"));

        var act = () => ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(attributes);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ReadEvidenceStages_NullAttributes_Throws()
    {
        var act = () => ServiceOrderPhotoEvidenceAttributes.ReadEvidenceStages(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
