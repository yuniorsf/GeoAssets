namespace GeoAssets.IpBilling.Models;

/// <summary>
/// Whether posting to the <see cref="Faturamento"/> ledger overwrites the monthly baseline
/// (<see cref="Set"/>, used by a validated Levantamento — recomputes the whole picture from
/// scratch), adds a delta to the running monthly total (<see cref="Accumulate"/>, used by a
/// validated Fiscalização/Ofício), or overwrites with an external ground-truth figure
/// (<see cref="Overwrite"/>, used by manual entry/batch load/recarga — XD01-189's legacy-parity
/// screens, not yet built; XD01-188 adds the service method they will call). <see cref="Set"/>
/// and <see cref="Overwrite"/> apply the same replace arithmetic to <c>KwhFaturado</c> — they're
/// kept as distinct enum values (not collapsed into one) purely for the audit trail's own
/// clarity: a reviewer reading <see cref="FaturamentoPosting.Mode"/> later should be able to
/// tell "recomputed from a survey" apart from "someone typed this in," even though both
/// mechanically replace the same column.
/// </summary>
public enum FaturamentoPostingMode
{
    Set = 0,
    Accumulate = 1,
    Overwrite = 2,
}
