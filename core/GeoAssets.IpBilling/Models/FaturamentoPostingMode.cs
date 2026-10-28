namespace GeoAssets.IpBilling.Models;

/// <summary>
/// Whether validating an IP-billing order overwrites the monthly baseline (<see cref="Set"/>,
/// used by Levantamento) or adds a delta to the running monthly total (<see cref="Accumulate"/>,
/// used by Fiscalização and Ofício).
/// </summary>
public enum FaturamentoPostingMode
{
    Set = 0,
    Accumulate = 1,
}
