using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using GeoAssets.Workflow.Contractors;

namespace GeoAssets.Workflow.Erp.SapWeBuy;

/// <summary>
/// Compiles a signed <see cref="EspelhoDeMedicaoDossierResult"/> (XD01-249) into the SAP WeBUY
/// supplier-portal clearing-package XML (XD01-255) — PO number, outline agreement number,
/// itemized BoQ, and per-item deep links to the evidence photos already hashed into the dossier.
///
/// Pure function over an already-resolved <see cref="SapWeBuyClearingPackageRequest"/> — no
/// repository/DI dependency, same "no hidden state" shape as
/// <see cref="EspelhoDeMedicaoDossierGenerator"/> itself.
/// </summary>
public static class SapWeBuyClearingPackageExporter
{
    public static SapWeBuyClearingPackageResult Export(SapWeBuyClearingPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Dossier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PoNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutlineAgreementNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EvidenceDeepLinkBaseUrl);

        var dossier = request.Dossier;
        if (dossier.Items.Count == 0)
        {
            throw new InvalidOperationException(
                $"Dossier for service order '{dossier.ServiceOrderId}' has no itemized BoQ lines; " +
                "cannot compile a WeBUY clearing package.");
        }

        var document = BuildDocument(request, dossier);
        ValidateAgainstSchema(document);

        var fileName = $"sap-webuy-clearing-package-{dossier.ServiceOrderId}.xml";
        return new SapWeBuyClearingPackageResult(dossier.ServiceOrderId, fileName, ToUtf8Bytes(document));
    }

    private static XDocument BuildDocument(SapWeBuyClearingPackageRequest request, EspelhoDeMedicaoDossierResult dossier)
    {
        var baseUrl = request.EvidenceDeepLinkBaseUrl.TrimEnd('/');
        var itemsElement = new XElement("Items", dossier.Items.Select(item => BuildItemElement(item, baseUrl)));

        var root = new XElement("WeBuyClearingPackage",
            new XAttribute("poNumber", request.PoNumber),
            new XAttribute("outlineAgreementNumber", request.OutlineAgreementNumber),
            new XAttribute("serviceOrderId", dossier.ServiceOrderId),
            new XAttribute("contractorOrganizationId", dossier.ContractorOrganizationId),
            new XAttribute("contractorName", dossier.ContractorName),
            new XAttribute("totalBrl", Decimal(dossier.TotalBrl)),
            new XAttribute("retentionBrl", Decimal(dossier.RetentionBrl)),
            new XAttribute("netPayableBrl", Decimal(dossier.NetPayableBrl)),
            new XAttribute("dossierSignature", dossier.SignatureBase64),
            itemsElement);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildItemElement(EspelhoDeMedicaoItemLine item, string baseUrl)
    {
        var itemElement = new XElement("Item",
            new XAttribute("itemCode", item.ItemCode),
            new XAttribute("quantity", Decimal(item.Quantity)),
            new XAttribute("unitUs", Decimal(item.UnitUs)),
            new XAttribute("totalUs", Decimal(item.TotalUs)),
            new XAttribute("totalBrl", Decimal(item.TotalBrl)));

        if (item.EvidencePhotoHashes.Count > 0)
        {
            itemElement.Add(new XElement("EvidenceDeepLinks",
                item.EvidencePhotoHashes.Select(hash => new XElement("DeepLink", $"{baseUrl}/{hash}"))));
        }

        return itemElement;
    }

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static void ValidateAgainstSchema(XDocument document)
    {
        var schemaSet = new XmlSchemaSet();
        var schemaReaderSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        using var schemaReader = XmlReader.Create(new StringReader(SapWeBuyClearingPackageSchema.Xsd), schemaReaderSettings);
        schemaSet.Add(string.Empty, schemaReader);

        var errors = new List<string>();
        document.Validate(schemaSet, (_, e) => errors.Add(e.Message));

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Compiled WeBUY clearing package failed its own schema validation: " + string.Join("; ", errors));
        }
    }

    private static byte[] ToUtf8Bytes(XDocument document)
    {
        using var stream = new MemoryStream();
        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };
        using var writer = XmlWriter.Create(stream, writerSettings);
        document.Save(writer);
        writer.Flush();
        return stream.ToArray();
    }
}
