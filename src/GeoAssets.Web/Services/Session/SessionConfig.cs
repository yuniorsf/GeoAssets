namespace GeoAssets.Web.Services.Session;

/// <summary>
/// Session and token expiration settings loaded from appsettings.json → "Session" section.
/// </summary>
public sealed class SessionConfig
{
    /// <summary>
    /// Minutes of inactivity before the session expires and the user is logged out.
    /// Default: 30 minutes.
    /// </summary>
    public int InactivityTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Seconds before the timeout at which the warning overlay is shown.
    /// Default: 60 seconds.
    /// </summary>
    public int WarningBeforeTimeoutSeconds { get; set; } = 60;
}
