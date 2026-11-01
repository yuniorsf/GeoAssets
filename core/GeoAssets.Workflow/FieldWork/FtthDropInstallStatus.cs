namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// String state keys for the FTTH drop-install lifecycle (XD01-273) — the
/// <see cref="GeoAssets.Workflow.Orders.OrderType.States"/>/<see cref="GeoAssets.Workflow.Orders.OrderType.Transitions"/>
/// graph <see cref="FtthDropInstallWorkflowGraph"/> builds. Its own, fully independent set of
/// constants — same reasoning as <see cref="FieldWorkStatus"/> keeping its own rather than
/// reusing <c>ServiceOrderStatus</c>'s.
/// </summary>
public static class FtthDropInstallStatus
{
    /// <summary>A CRM sale has been recorded; the drop-install order has just been created.</summary>
    public const string CrmSale = "CrmSale";

    /// <summary>The feeding CTO has been assigned and its splitter port tested.</summary>
    public const string CtoPortTest = "CtoPortTest";

    /// <summary>The drop cable has been physically launched from the CTO toward the customer premise.</summary>
    public const string DropLaunch = "DropLaunch";

    /// <summary>The drop cable has been tagged/labeled at both ends.</summary>
    public const string TagAffixed = "TagAffixed";

    /// <summary>
    /// The PTO has been powered up and verified — gated by a passing CTO OPM reading already
    /// recorded on the order (see <see cref="FtthDropInstallTransitioner"/>).
    /// </summary>
    public const string PtoPower = "PtoPower";

    /// <summary>The ONT has been provisioned against the customer's service plan.</summary>
    public const string OntProvisioned = "OntProvisioned";

    /// <summary>The drop is live — this graph's success-terminal state.</summary>
    public const string Activated = "Activated";
}
