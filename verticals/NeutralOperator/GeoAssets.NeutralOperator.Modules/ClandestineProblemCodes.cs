namespace GeoAssets.NeutralOperator.Modules;

/// <summary>The problem codes <see cref="ClandestineNoticeActionHandler"/> answers with (XD01-364).</summary>
public static class ClandestineProblemCodes
{
    /// <summary>Regularizing a notice whose statutory window has already elapsed (409).</summary>
    public const string WindowElapsed = "clandestine.window-elapsed";

    /// <summary>A dispute without its evidence reference in the entry's comment (400).</summary>
    public const string EvidenceRequired = "clandestine.evidence-required";
}
