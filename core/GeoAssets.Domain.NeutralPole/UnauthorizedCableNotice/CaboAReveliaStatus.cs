namespace GeoAssets.Domain.NeutralPole.UnauthorizedCableNotice;

/// <summary>Lifecycle of one "Cabo à Revelia" (unauthorized cable) notice under Decreto 12.068/2024's 30-day cure window.</summary>
public enum CaboAReveliaStatus
{
    /// <summary>An unauthorized cable was found on the pole, but no formal notice has been issued yet.</summary>
    Detected,

    /// <summary>The 30-day notice has been issued; the clock is running.</summary>
    NoticeIssued,

    /// <summary>The cable was regularized or removed.</summary>
    Resolved,

    /// <summary>The 30-day window passed with no resolution.</summary>
    Expired,
}
