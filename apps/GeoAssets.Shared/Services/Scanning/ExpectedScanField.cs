namespace GeoAssets.Shared.Services.Scanning;

/// <summary>
/// Which field a barcode/QR scan is being applied to — the FTTH drop-install mobile checklist
/// (XD01-275) selects this up front (e.g. "scan the yellow tag" vs. "scan the ONT's FSAN label"),
/// so <see cref="FieldWorkBarcodeScanValidator"/> validates/normalizes against the one expected
/// format rather than trying to guess it from the payload's shape alone.
/// </summary>
public enum ExpectedScanField
{
    /// <summary>The yellow identification tag affixed to the drop (XD01-273's <c>TagAffixed</c> state).</summary>
    YellowTagId,

    /// <summary>The ONT's printed FSAN serial.</summary>
    OntFsan,

    /// <summary>The ONT's printed MAC address.</summary>
    OntMacAddress,
}
