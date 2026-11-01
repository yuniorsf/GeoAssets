using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class TechnicalNormSchemasTests
{
    private static Dictionary<string, string> Attributes(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return dict;
    }

    private static readonly OrderType VegetationOrderType =
        TechnicalNormSchemas.BuildVegetationClearanceOrderType("vegetation-clearance", "Vegetation Clearance");

    private static readonly OrderType AnomalyOrderType =
        TechnicalNormSchemas.BuildAnomalySlaOrderType("anomaly-sla", "Anomaly SLA");

    private static readonly OrderType MaterialOrderType =
        TechnicalNormSchemas.BuildMaterialCertificationOrderType("material-certification", "Material Certification");

    // ── VegetationClearanceSchema ────────────────────────────────────────────────

    [Fact]
    public void Vegetation_AllGatesTrue_IsValid()
    {
        var attributes = Attributes(
            ("minimumClearanceMaintained", "true"),
            ("vegetationSpeciesIdentified", "true"),
            ("clearanceVerifiedBy", "tech-1"));

        ServiceOrderAttributeValidator.Validate(VegetationOrderType, attributes).Should().BeEmpty();
    }

    [Fact]
    public void Vegetation_ClearanceNotMaintained_IsInvalid()
    {
        var attributes = Attributes(
            ("minimumClearanceMaintained", "false"),
            ("vegetationSpeciesIdentified", "true"),
            ("clearanceVerifiedBy", "tech-1"));

        ServiceOrderAttributeValidator.Validate(VegetationOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Vegetation_VerifiedByMissing_IsInvalid()
    {
        var attributes = Attributes(
            ("minimumClearanceMaintained", "true"),
            ("vegetationSpeciesIdentified", "true"));

        ServiceOrderAttributeValidator.Validate(VegetationOrderType, attributes).Should().NotBeEmpty();
    }

    // ── AnomalySlaSchema ──────────────────────────────────────────────────────────

    [Fact]
    public void Anomaly_AllFieldsValid_IsValid()
    {
        var attributes = Attributes(
            ("anomalyCode", "AN-042"),
            ("slaDeadlineUtc", "2026-11-05T12:00:00Z"),
            ("slaAcknowledged", "true"));

        ServiceOrderAttributeValidator.Validate(AnomalyOrderType, attributes).Should().BeEmpty();
    }

    [Fact]
    public void Anomaly_NotAcknowledged_IsInvalid()
    {
        var attributes = Attributes(
            ("anomalyCode", "AN-042"),
            ("slaDeadlineUtc", "2026-11-05T12:00:00Z"),
            ("slaAcknowledged", "false"));

        ServiceOrderAttributeValidator.Validate(AnomalyOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Anomaly_MalformedSlaDeadline_IsInvalid()
    {
        var attributes = Attributes(
            ("anomalyCode", "AN-042"),
            ("slaDeadlineUtc", "not-a-timestamp"),
            ("slaAcknowledged", "true"));

        ServiceOrderAttributeValidator.Validate(AnomalyOrderType, attributes).Should().NotBeEmpty();
    }

    // ── MaterialCertificationSchema ───────────────────────────────────────────────

    [Fact]
    public void Material_AllFieldsValid_IsValid()
    {
        var attributes = Attributes(
            ("materialLotNumber", "LOT-123"),
            ("compactionCertificateNumber", "CERT-456"),
            ("compactionPercentage", "95.5"));

        ServiceOrderAttributeValidator.Validate(MaterialOrderType, attributes).Should().BeEmpty();
    }

    [Theory]
    [InlineData("89.9")]
    [InlineData("100.1")]
    public void Material_CompactionPercentageOutOfRange_IsInvalid(string percentage)
    {
        var attributes = Attributes(
            ("materialLotNumber", "LOT-123"),
            ("compactionCertificateNumber", "CERT-456"),
            ("compactionPercentage", percentage));

        ServiceOrderAttributeValidator.Validate(MaterialOrderType, attributes).Should().NotBeEmpty();
    }

    // ── AC #1/#2: a single validator correctly switches schemas purely from the order's own OrderTypeId ──

    [Fact]
    public void SameValidator_AppliesEachOrdersOwnSchema_WithNoCrossContamination()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(VegetationOrderType);
        registry.Register(AnomalyOrderType);

        var vegetationAttributes = Attributes(
            ("minimumClearanceMaintained", "true"),
            ("vegetationSpeciesIdentified", "true"),
            ("clearanceVerifiedBy", "tech-1"));
        var anomalyAttributes = Attributes(
            ("anomalyCode", "AN-042"),
            ("slaDeadlineUtc", "2026-11-05T12:00:00Z"),
            ("slaAcknowledged", "true"));

        // A technician's tablet validating a Coelba-style order, then a CPFL-style order, in the
        // same shift — each resolved purely from its own OrderTypeId, no shared/sticky state.
        var vegetationOrderType = registry.Find(VegetationOrderType.Id)!;
        ServiceOrderAttributeValidator.Validate(vegetationOrderType, anomalyAttributes).Should().NotBeEmpty(
            "the vegetation schema must reject anomaly-shaped attributes");

        var anomalyOrderType = registry.Find(AnomalyOrderType.Id)!;
        ServiceOrderAttributeValidator.Validate(anomalyOrderType, vegetationAttributes).Should().NotBeEmpty(
            "the anomaly schema must reject vegetation-shaped attributes");

        ServiceOrderAttributeValidator.Validate(vegetationOrderType, vegetationAttributes).Should().BeEmpty();
        ServiceOrderAttributeValidator.Validate(anomalyOrderType, anomalyAttributes).Should().BeEmpty();
    }

    // ── AC #3: a new concessionaire's rule set is config/data, never a code change ──

    [Fact]
    public void NewSchema_AddedPurelyAsRuntimeData_ValidatesThroughTheExactSameUnchangedPipeline()
    {
        // Stands in for "a new concessionaire's norm, added via IOrderTypeRepository.AddAsync" —
        // a JSON string built here, at the test, never a new type/method in production code.
        const string newConcessionaireSchema = """
        {
          "type": "object",
          "properties": { "newNormFieldAcknowledged": { "const": true } },
          "required": ["newNormFieldAcknowledged"],
          "additionalProperties": false
        }
        """;
        var orderType = FieldWorkWorkflowGraph.BuildOrderType("new-concessionaire-norm", "New Concessionaire Norm", attributesSchemaJson: newConcessionaireSchema);
        var registry = new OrderTypeRegistry();
        registry.Register(orderType);

        var resolved = registry.Find("new-concessionaire-norm")!;
        ServiceOrderAttributeValidator.Validate(resolved, Attributes(("newNormFieldAcknowledged", "true"))).Should().BeEmpty();
        ServiceOrderAttributeValidator.Validate(resolved, Attributes(("newNormFieldAcknowledged", "false"))).Should().NotBeEmpty();
    }
}
