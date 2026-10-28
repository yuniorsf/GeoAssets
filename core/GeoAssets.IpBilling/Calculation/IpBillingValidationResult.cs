using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

public sealed record IpBillingValidationResult(
    string OrderId,
    string Status,
    Faturamento Faturamento,
    FaturamentoPostingMode PostingMode);
