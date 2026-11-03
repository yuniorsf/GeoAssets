namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

/// <summary>
/// The per-fixation-point monthly lease rate for a Neutral Infrastructure Operator's "Poste
/// Justo" billing (XD01-267). No published Poste Justo tariff schedule exists anywhere in this
/// codebase or repo — <see cref="TariffPerFixationPoint"/> is an explicit, caller-supplied
/// configuration value specifically so a real rate can be plugged in later without any code
/// change. Any value used in this module's own tests is illustrative only, not a real tariff.
/// </summary>
public sealed record LeaseTariffSchedule(decimal TariffPerFixationPoint);
