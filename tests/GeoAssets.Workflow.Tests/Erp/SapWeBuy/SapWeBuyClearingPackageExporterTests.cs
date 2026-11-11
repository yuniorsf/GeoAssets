using System.Xml.Linq;
using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.SapWeBuy;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.SapWeBuy;

public class SapWeBuyClearingPackageExporterTests
{
    private static EspelhoDeMedicaoDossierResult Dossier(IReadOnlyList<EspelhoDeMedicaoItemLine>? items = null) => new(
        ServiceOrderId: "order-1",
        ContractorOrganizationId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ContractorName: "Empreiteira Teste",
        ServiceOrderTitle: "Troca de poste",
        ExecutionStartedAtUtc: new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
        ExecutionCompletedAtUtc: new DateTime(2026, 1, 20, 17, 0, 0, DateTimeKind.Utc),
        Items: items ?? [new EspelhoDeMedicaoItemLine("US-001", 2m, 1.5m, 3m, 10m, 30m, ["abc123", "def456"])],
        TotalBrl: 30m,
        RetentionRate: 0.1m,
        RetentionBrl: 3m,
        NetPayableBrl: 27m,
        AprChecklistSignedAtUtc: null,
        DocumentText: "irrelevant for this adapter",
        SignatureBase64: "c2lnbmF0dXJl");

    private static SapWeBuyClearingPackageRequest Request(
        EspelhoDeMedicaoDossierResult? dossier = null,
        string poNumber = "PO-4500012345",
        string outlineAgreementNumber = "OA-5500098765",
        string baseUrl = "https://evidence.geoassets.example/photos") =>
        new(dossier ?? Dossier(), poNumber, outlineAgreementNumber, baseUrl);

    [Fact]
    public void Export_ProducesWellFormedXmlWithRequiredReferenceFields()
    {
        var result = SapWeBuyClearingPackageExporter.Export(Request());

        result.ServiceOrderId.Should().Be("order-1");
        result.FileName.Should().Be("sap-webuy-clearing-package-order-1.xml");

        var root = XDocument.Load(new MemoryStream(result.XmlBytes)).Root!;
        root.Name.LocalName.Should().Be("WeBuyClearingPackage");
        root.Attribute("poNumber")!.Value.Should().Be("PO-4500012345");
        root.Attribute("outlineAgreementNumber")!.Value.Should().Be("OA-5500098765");
        root.Attribute("serviceOrderId")!.Value.Should().Be("order-1");
        root.Attribute("dossierSignature")!.Value.Should().Be("c2lnbmF0dXJl");
    }

    [Fact]
    public void Export_EmitsOneItemElementPerItemizedBoqLine()
    {
        var items = new EspelhoDeMedicaoItemLine[]
        {
            new("US-001", 2m, 1.5m, 3m, 10m, 30m, ["hash-a"]),
            new("US-002", 1m, 4m, 4m, 5m, 20m, []),
        };

        var result = SapWeBuyClearingPackageExporter.Export(Request(Dossier(items)));

        var root = XDocument.Load(new MemoryStream(result.XmlBytes)).Root!;
        var itemElements = root.Element("Items")!.Elements("Item").ToList();

        itemElements.Should().HaveCount(2);
        itemElements[0].Attribute("itemCode")!.Value.Should().Be("US-001");
        itemElements[1].Attribute("itemCode")!.Value.Should().Be("US-002");
    }

    [Fact]
    public void Export_BuildsOneDeepLinkPerEvidencePhotoHashUnderTheConfiguredBaseUrl()
    {
        var items = new EspelhoDeMedicaoItemLine[] { new("US-001", 2m, 1.5m, 3m, 10m, 30m, ["hash-a", "hash-b"]) };

        var result = SapWeBuyClearingPackageExporter.Export(
            Request(Dossier(items), baseUrl: "https://evidence.geoassets.example/photos/"));

        var root = XDocument.Load(new MemoryStream(result.XmlBytes)).Root!;
        var deepLinks = root.Element("Items")!.Element("Item")!.Element("EvidenceDeepLinks")!
            .Elements("DeepLink").Select(e => e.Value).ToList();

        deepLinks.Should().Equal(
            "https://evidence.geoassets.example/photos/hash-a",
            "https://evidence.geoassets.example/photos/hash-b");
    }

    [Fact]
    public void Export_OmitsEvidenceDeepLinksElementWhenAnItemHasNoPhotoEvidence()
    {
        var items = new EspelhoDeMedicaoItemLine[] { new("US-001", 2m, 1.5m, 3m, 10m, 30m, []) };

        var result = SapWeBuyClearingPackageExporter.Export(Request(Dossier(items)));

        var root = XDocument.Load(new MemoryStream(result.XmlBytes)).Root!;
        root.Element("Items")!.Element("Item")!.Element("EvidenceDeepLinks").Should().BeNull();
    }

    [Theory]
    [InlineData("", "OA-1")]
    [InlineData(" ", "OA-1")]
    [InlineData("PO-1", "")]
    [InlineData("PO-1", " ")]
    public void Export_RejectsBlankPoNumberOrOutlineAgreementNumber(string poNumber, string outlineAgreementNumber)
    {
        var act = () => SapWeBuyClearingPackageExporter.Export(Request(poNumber: poNumber, outlineAgreementNumber: outlineAgreementNumber));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Export_RejectsADossierWithNoItemizedBoqLines()
    {
        var act = () => SapWeBuyClearingPackageExporter.Export(Request(Dossier(items: [])));

        act.Should().Throw<InvalidOperationException>().WithMessage("*no itemized BoQ lines*");
    }
}
