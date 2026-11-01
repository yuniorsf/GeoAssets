namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>
/// Per-indicator multipliers for <see cref="CompensationCalculator"/>'s excess-ratio
/// compensation formula.
///
/// <b>Not sourced here.</b> PRODIST Módulo 8 Submódulo 8's actual published coefficients (and
/// the formula's exact shape — standalone per-indicator vs. a combined DIC+FIC+DMIC term, the
/// definition of the reference billing value they multiply, and Grupo A vs. Grupo B variants)
/// are set and periodically revised by ANEEL. Hardcoding a specific set of numbers here would
/// present an unverified, possibly-stale value as authoritative for a real financial payout —
/// instead, <see cref="CompensationCalculator"/> takes this record as an explicit parameter.
/// Source actual values from the current official PRODIST technical note (or the distributor's
/// own regulatory filing) before using this for anything beyond structural testing.
/// </summary>
public sealed record CompensationCoefficients(decimal Dic, decimal Fic, decimal Dmic);
