namespace GeoAssets.FieldService.Server;

/// <summary>
/// The Neutral Infrastructure Operator's own PIX receiving identity — tenant ISPs pay these
/// lease invoices TO the operator, so this is one fixed merchant identity, not per-tenant.
/// </summary>
internal sealed class PixSettlementOptions
{
    public const string SectionName = "Isp:LeaseSettlement:Pix";

    public string PixKey { get; set; } = string.Empty;

    public string MerchantName { get; set; } = string.Empty;

    public string MerchantCity { get; set; } = string.Empty;
}
