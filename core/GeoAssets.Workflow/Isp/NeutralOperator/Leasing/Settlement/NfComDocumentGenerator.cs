using System.Globalization;
using System.Xml.Linq;
using GeoAssets.Core.Isp.NeutralOperator.Leasing;

namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Generates an NF Com (Nota Fiscal de Serviços de Comunicação — the Brazilian telecom-service
/// tax invoice) document for one <see cref="LeaseInvoice"/>.
///
/// The real NF Com is layout 4.00, issued against SEFAZ's live web service under a digital
/// certificate (A1/A3), with SEFAZ-assigned series/number and UF-specific endpoints — none of
/// which this environment has access to or can responsibly fabricate. This generator instead
/// produces a complete, internally-consistent, deterministic XML document — every invoice line
/// item, the tenant, the period, the total — against a GeoAssets-authored placeholder element
/// set (see <see cref="PlaceholderNamespace"/>), explicitly not SEFAZ's schema. A human must
/// swap in the real NFCom 4.00 schema, signing, and SEFAZ submission before this reaches
/// production — the same stance XD01-255 took for SAP WeBUY's confidential XSD.
/// </summary>
public sealed class NfComDocumentGenerator : INfComDocumentGenerator
{
    /// <summary>Not a real SEFAZ namespace — flags this document as GeoAssets's own placeholder shape.</summary>
    public const string PlaceholderNamespace = "urn:geoassets:placeholder:nfcom-pending-sefaz-integration";

    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _documentNumberFactory;

    public NfComDocumentGenerator(TimeProvider timeProvider, Func<string>? documentNumberFactory = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _documentNumberFactory = documentNumberFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public Task<NfComDocumentResult> GenerateAsync(LeaseInvoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ct.ThrowIfCancellationRequested();

        var documentNumber = _documentNumberFactory();
        var issuedAtUtc = _timeProvider.GetUtcNow();
        XNamespace ns = PlaceholderNamespace;

        var document = new XDocument(
            new XElement(ns + "nfComPlaceholder",
                new XAttribute("documentNumber", documentNumber),
                new XAttribute("issuedAtUtc", issuedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                new XElement(ns + "tenant", new XAttribute("cnpj", invoice.TenantCnpj)),
                new XElement(ns + "billingPeriod", invoice.BillingPeriod.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
                new XElement(ns + "items",
                    invoice.LineItems.Select(item =>
                        new XElement(ns + "item",
                            new XAttribute("kind", item.Kind),
                            new XAttribute("amount", item.Amount.ToString(CultureInfo.InvariantCulture)),
                            item.Description))),
                new XElement(ns + "total", invoice.Total.ToString(CultureInfo.InvariantCulture))));

        return Task.FromResult(new NfComDocumentResult(documentNumber, document.ToString(SaveOptions.DisableFormatting), issuedAtUtc));
    }
}
