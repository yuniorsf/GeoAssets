using GeoAssets.NeutralOperator.Domain.Leasing;

namespace GeoAssets.NeutralOperator.Application.Leasing.Settlement;

/// <summary>Generates the NF Com document for one lease invoice. See <see cref="NfComDocumentResult"/>.</summary>
public interface INfComDocumentGenerator
{
    Task<NfComDocumentResult> GenerateAsync(LeaseInvoice invoice, CancellationToken ct);
}
