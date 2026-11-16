using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Shows what a validated Fiscalização/Ofício posted to the Faturamento ledger (XD01-301): the
/// signed kWh delta, the ledger row's resulting total, and — when any point was converted with
/// the chart's mean watts-per-point rather than its own lamp detail — a prominent
/// "provisional" badge. Parameter-down only; both detail components feed it from their
/// validation result.
/// </summary>
public partial class InspectionPostingSummary
{
    /// <summary>False for an order validated before kWh posting existed — it has no posting.</summary>
    [Parameter] public bool HasPosting { get; set; }
    [Parameter] public decimal DeltaKwh { get; set; }
    [Parameter] public decimal? LedgerKwh { get; set; }
    [Parameter] public bool IsProvisional { get; set; }
    [Parameter] public int ExactPoints { get; set; }
    [Parameter] public int ProvisionalPoints { get; set; }

    /// <summary>Ledger precision (4 decimal places) in the current UI culture; a credit is negative.</summary>
    public static string FormatKwh(decimal kwh) => kwh.ToString("N4", CultureInfo.CurrentCulture);
}
