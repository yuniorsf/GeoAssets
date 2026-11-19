using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseImportMappingTests
{
    [Fact]
    public void From_IndexesEveryPart_ByLegacyIdAndCnpjDigits_WithMeasurementsRounded()
    {
        var mapping = SharedUseImportMapping.From(Document(
            poles: [new(1, "pole-1"), new(2, "pole-1")],
            occupants: [new(IdentifiedOccupant, IdentifiedCnpj), new(OccupantWithoutCnpj, null)],
            organizations: [new("10.000.000/0001-01", OccupantOrganization)],
            wireTypes: [new(WireType, 12.3456m, 1.2345m)]));

        mapping.PoleFeatureIds.Should().BeEquivalentTo(new Dictionary<int, string> { [1] = "pole-1", [2] = "pole-1" });
        mapping.OccupantCnpjs.Should().BeEquivalentTo(new Dictionary<int, string?> { [IdentifiedOccupant] = IdentifiedCnpj, [OccupantWithoutCnpj] = null });
        mapping.OrganizationsByCnpj.Should().BeEquivalentTo(new Dictionary<string, Guid>(StringComparer.Ordinal) { ["10000000000101"] = OccupantOrganization });
        mapping.WireTypes[WireType].Should().Be(new WireTypeMapping(WireType, 12.346m, 1.235m));
    }

    [Fact]
    public void From_EmptyDocument_IsAnEmptyMapping()
    {
        var mapping = SharedUseImportMapping.From(new SharedUseImportMappingDocument([], [], [], []));

        mapping.PoleFeatureIds.Should().BeEmpty();
        mapping.OccupantCnpjs.Should().BeEmpty();
        mapping.OrganizationsByCnpj.Should().BeEmpty();
        mapping.WireTypes.Should().BeEmpty();
    }

    [Fact]
    public void From_NullDocument_Throws()
    {
        var act = () => SharedUseImportMapping.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("1.0005", "1.001")]
    [InlineData("1.0015", "1.002")]
    [InlineData("2.0004", "2.000")]
    [InlineData("12.3456", "12.346")]
    [InlineData("7", "7")]
    [InlineData("-0.0004", "0")]
    public void RoundMeasurement_KeepsThreeDecimals_HalvesAwayFromZero(string value, string expected)
    {
        var rounded = SharedUseImportMapping.RoundMeasurement(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        rounded.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        rounded.Scale.Should().BeLessThanOrEqualTo(PoleOccupancyEntry.MeasurementDecimals);
    }

    [Theory]
    [InlineData("10.000.000/0001-01", "10000000000101")]
    [InlineData("10000000000101", "10000000000101")]
    [InlineData("--", "")]
    public void CnpjDigits_DropsEverythingButDigits(string cnpj, string expected)
    {
        SharedUseImportMapping.CnpjDigits(cnpj).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_BlankPoleFeatureId_Throws(string featureId)
    {
        var act = () => SharedUseImportMapping.From(Document(poles: [new(1, featureId)]));

        act.Should().Throw<InvalidDataException>().WithMessage("Legacy pole 1: the pole feature id*");
    }

    [Fact]
    public void From_PoleFeatureIdLongerThanTheStoreAccepts_Throws_ButTheLongestAcceptedPasses()
    {
        var longest = new string('p', PoleOccupancyEntry.MaxFeatureIdLength);

        var accepted = () => SharedUseImportMapping.From(Document(poles: [new(1, longest)]));
        var refused = () => SharedUseImportMapping.From(Document(poles: [new(1, longest + "p")]));

        accepted.Should().NotThrow();
        refused.Should().Throw<InvalidDataException>().WithMessage("Legacy pole 1: the pole feature id*");
    }

    [Fact]
    public void From_LegacyPoleMappedTwice_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(poles: [new(1, "pole-1"), new(1, "pole-2")]));

        act.Should().Throw<InvalidDataException>().WithMessage("Legacy pole 1 is mapped more than once.");
    }

    [Fact]
    public void From_MappingTheUnidentifiedOccupant_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(occupants: [new(LegacySharedUseRow.UnidentifiedOccupantId, IdentifiedCnpj)]));

        act.Should().Throw<InvalidDataException>().WithMessage("*unidentified occupant*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("10.000.000/0001-010")]
    public void From_OccupantCnpjBlankOrTooLong_Throws(string cnpj)
    {
        var act = () => SharedUseImportMapping.From(Document(occupants: [new(IdentifiedOccupant, cnpj)]));

        act.Should().Throw<InvalidDataException>().WithMessage($"Legacy occupant {IdentifiedOccupant}: a CNPJ*");
    }

    [Fact]
    public void From_LegacyOccupantMappedTwice_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(occupants: [new(IdentifiedOccupant, IdentifiedCnpj), new(IdentifiedOccupant, null)]));

        act.Should().Throw<InvalidDataException>().WithMessage($"Legacy occupant {IdentifiedOccupant} is mapped more than once.");
    }

    [Theory]
    [InlineData("1000000000010")]
    [InlineData("100000000001011")]
    [InlineData("")]
    public void From_OrganizationCnpjWithoutFourteenDigits_Throws(string cnpj)
    {
        var act = () => SharedUseImportMapping.From(Document(organizations: [new(cnpj, OccupantOrganization)]));

        act.Should().Throw<InvalidDataException>().WithMessage("*does not have 14 digits*");
    }

    [Fact]
    public void From_OrganizationWithEmptyId_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(organizations: [new("10000000000101", Guid.Empty)]));

        act.Should().Throw<InvalidDataException>().WithMessage("*empty organization id*");
    }

    [Fact]
    public void From_SameCnpjTwice_EvenPunctuatedDifferently_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(organizations:
            [new("10000000000101", OccupantOrganization), new("10.000.000/0001-01", Guid.NewGuid())]));

        act.Should().Throw<InvalidDataException>().WithMessage("*mapped more than once*");
    }

    [Theory]
    [InlineData("-0.001", "0", "cable diameter")]
    [InlineData("0", "-0.001", "cable load")]
    [InlineData("99999.9995", "0", "cable diameter")]
    [InlineData("0", "999999999.9995", "cable load")]
    public void From_WireTypeMeasurementOutOfRangeOnceRounded_Throws(string diameter, string load, string which)
    {
        var act = () => SharedUseImportMapping.From(Document(wireTypes:
            [new(WireType, decimal.Parse(diameter, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(load, System.Globalization.CultureInfo.InvariantCulture))]));

        act.Should().Throw<InvalidDataException>().WithMessage($"Legacy wire type {WireType}: the {which}*");
    }

    [Fact]
    public void From_WireTypeMeasurementsThatRoundToTheMaximum_AreAccepted()
    {
        var mapping = SharedUseImportMapping.From(Document(wireTypes: [new(WireType, 99_999.9994m, 999_999_999.9994m)]));

        mapping.WireTypes[WireType].Should().Be(new WireTypeMapping(WireType, PoleOccupancyEntry.MaxCableDiameterMm, PoleOccupancyEntry.MaxCableLoadDaN));
    }

    [Fact]
    public void From_WireTypeMappedTwice_Throws()
    {
        var act = () => SharedUseImportMapping.From(Document(wireTypes: [new(WireType, 1m, 1m), new(WireType, 2m, 2m)]));

        act.Should().Throw<InvalidDataException>().WithMessage($"Legacy wire type {WireType} is mapped more than once.");
    }
}
