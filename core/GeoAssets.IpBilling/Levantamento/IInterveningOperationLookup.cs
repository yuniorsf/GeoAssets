namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// The [FIX] hook codes 01/11 need: whether a validated Fiscalização or Ofício already billed
/// some of the kWh a Levantamento's retroactive charge would otherwise double-count.
///
/// <c>Fiscalizacao</c>/<c>Oficio</c>Line entities don't exist yet — they're Phase 2/3
/// deliverables (XD01-182/185), which come <em>after</em> this ticket in the epic's own build
/// order, even though this ticket's own calculation needs to query them. Exposed as an
/// interface for exactly this reason, mirroring this ticket's own "expose the hook, wire it
/// later" treatment of the Faturamento ledger call — <see cref="NullInterveningOperationLookup"/>
/// is the default binding until XD01-182/185 provide a real implementation backed by their own
/// persisted lines.
/// </summary>
public interface IInterveningOperationLookup
{
    /// <summary>
    /// The most recent validated Fiscalização/Ofício coverage for
    /// <paramref name="codigoInstalacao"/> (or município-wide when <c>null</c> — see this
    /// ticket's "10/11 anchor" business-owner checkpoint) strictly before <paramref name="before"/>.
    /// <c>null</c> when none exists.
    /// </summary>
    Task<InterveningOperationCoverage?> GetMostRecentValidatedAsync(
        Guid municipioId, string? codigoInstalacao, DateOnly before, CancellationToken ct = default);
}
