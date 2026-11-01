using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

public class ServiceOrderBoqAttributesTests
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

    // ── ReadContractorOrganizationId ─────────────────────────────────────────────

    [Fact]
    public void ReadContractorOrganizationId_ValidGuid_ReturnsIt()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var attributes = Attributes((ServiceOrderBoqAttributes.ContractorOrganizationIdKey, id.ToString()));

        ServiceOrderBoqAttributes.ReadContractorOrganizationId(attributes).Should().Be(id);
    }

    [Fact]
    public void ReadContractorOrganizationId_Missing_Throws()
    {
        var act = () => ServiceOrderBoqAttributes.ReadContractorOrganizationId(Attributes());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReadContractorOrganizationId_NotAGuid_Throws()
    {
        var attributes = Attributes((ServiceOrderBoqAttributes.ContractorOrganizationIdKey, "not-a-guid"));

        var act = () => ServiceOrderBoqAttributes.ReadContractorOrganizationId(attributes);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ReadBoqLines ──────────────────────────────────────────────────────────────

    [Fact]
    public void ReadBoqLines_Missing_ReturnsEmpty()
    {
        ServiceOrderBoqAttributes.ReadBoqLines(Attributes()).Should().BeEmpty();
    }

    [Fact]
    public void ReadBoqLines_SingleLine_ParsesItemCodeAndQuantity()
    {
        var attributes = Attributes((ServiceOrderBoqAttributes.BoqLinesKey, """[{"itemCode":"US-EL-014","quantity":2.5}]"""));

        var lines = ServiceOrderBoqAttributes.ReadBoqLines(attributes);

        lines.Should().ContainSingle();
        lines[0].ItemCode.Should().Be("US-EL-014");
        lines[0].Quantity.Should().Be(2.5m);
    }

    [Fact]
    public void ReadBoqLines_MultipleLines_ParsesAllInOrder()
    {
        var attributes = Attributes((ServiceOrderBoqAttributes.BoqLinesKey,
            """[{"itemCode":"US-EL-014","quantity":2},{"itemCode":"US-EL-022","quantity":3}]"""));

        var lines = ServiceOrderBoqAttributes.ReadBoqLines(attributes);

        lines.Should().HaveCount(2);
        lines[0].ItemCode.Should().Be("US-EL-014");
        lines[1].ItemCode.Should().Be("US-EL-022");
    }

    [Fact]
    public void ReadBoqLines_MalformedJson_Throws()
    {
        var attributes = Attributes((ServiceOrderBoqAttributes.BoqLinesKey, "not json"));

        var act = () => ServiceOrderBoqAttributes.ReadBoqLines(attributes);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ReadContractorOrganizationId_NullAttributes_Throws()
    {
        var act = () => ServiceOrderBoqAttributes.ReadContractorOrganizationId(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadBoqLines_NullAttributes_Throws()
    {
        var act = () => ServiceOrderBoqAttributes.ReadBoqLines(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
