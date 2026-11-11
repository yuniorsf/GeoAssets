using GeoAssets.Core.Isp.NeutralOperator.Leasing;

namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>Generates the NF Com document for one lease invoice. See <see cref="NfComDocumentResult"/>.</summary>
public interface INfComDocumentGenerator
{
    Task<NfComDocumentResult> GenerateAsync(LeaseInvoice invoice, CancellationToken ct);
}
