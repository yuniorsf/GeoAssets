using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// <see cref="ServiceOrder.Attributes"/> keys the FTTH drop-install checklist (XD01-277) reads
/// and writes as it walks a drop order through <see cref="FtthDropInstallWorkflowGraph"/>.
/// </summary>
public static class FtthDropInstallChecklistAttributes
{
    /// <summary>
    /// The CTO feeding this drop — expected to already be set on the order at creation time
    /// (e.g. by whatever process used <c>NearestAvailableCtoAssigner</c>, XD01-273, to assign
    /// it), not written by the checklist itself; the checklist's own flow starts at "CTO
    /// arrival," implying the CTO is already known by then.
    /// </summary>
    public const string CtoFeatureIdKey = "ctoFeatureId";

    /// <summary>The CTO optical power meter reading, recorded during the CtoPortTest step.</summary>
    public const string CtoReadingDbmKey = "ctoReadingDbm";

    /// <summary>The PTO optical power meter reading, recorded entering the PtoPower step.</summary>
    public const string PtoReadingDbmKey = "ptoReadingDbm";

    /// <summary>The scanned yellow identification tag ID, recorded during the TagAffixed step.</summary>
    public const string YellowTagIdKey = "yellowTagId";

    /// <summary>The scanned ONT FSAN serial, recorded during the OntProvisioned step.</summary>
    public const string OntFsanKey = "ontFsan";

    /// <summary>The scanned ONT MAC address, recorded during the OntProvisioned step.</summary>
    public const string OntMacAddressKey = "ontMacAddress";
}
