namespace GeoAssets.Workflow.SlaGuard;

/// <summary>
/// One row of a statutory SLA deadline matrix (XD01-226): the maximum duration allowed for
/// <paramref name="ServiceCategory"/> (e.g. "EmergencyRestoration", "UrgentReconnection",
/// "SurgeInspection", "MmgdSurvey", "IsapLeak" — the categories XD01-226's own summary names)
/// in <paramref name="TerritorialSetting"/> (e.g. "Urban", "Rural").
///
/// <paramref name="MaxDuration"/> is always caller-supplied, never a constant this engine
/// presents as itself authoritative — the real REN 1.000/Sabesp ISAP durations in force are a
/// regulatory fact to source from the current official text, not something to hardcode here and
/// have silently go stale. Same posture as XD01-209/210/211's regulatory domain assemblies and
/// XD01-222's safety-checklist thresholds.
/// </summary>
public sealed record SlaMatrixRow(string ServiceCategory, string TerritorialSetting, TimeSpan MaxDuration);
