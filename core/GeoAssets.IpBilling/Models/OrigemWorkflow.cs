namespace GeoAssets.IpBilling.Models;

/// <summary>
/// Where a município's billable kWh for one of the four IP-billing workflows currently
/// originates from: a spreadsheet upload (this epic's in-scope path) or a dispatched
/// <c>ServiceOrder</c> fan-out (the out-of-scope "service-order graduation" path described
/// on the parent epic, XD01-175 — reusing the same <c>OrderType</c>s either way, so a
/// município can switch per-workflow with no schema change).
/// </summary>
public enum OrigemWorkflow
{
    /// <summary>Data for this workflow arrives via GeoAssets.Provider.Xlsx spreadsheet upload.</summary>
    Planilha,

    /// <summary>Data for this workflow arrives via dispatched service orders (out of scope for this epic).</summary>
    OrdemServico,
}
