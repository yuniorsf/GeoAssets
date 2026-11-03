using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// The legacy-parity manual entry / batch load / recarga write paths (XD01-189), each composing
/// <see cref="IFaturamentoRepository.GetByMunicipioAndCompetenciaAsync"/> (existence check) with
/// <see cref="IFaturamentoLedgerService.OverwriteManualEntryAsync"/> (the actual write) under a
/// different existence policy per operation:
///
/// <list type="bullet">
/// <item>Manual entry: insert-only, REJECTS (throws <see cref="FaturamentoAlreadyPostedException"/>)
/// if the pair already exists.</item>
/// <item>Batch load: insert-only, SKIPS (and reports) pairs that already exist.</item>
/// <item>Recarga: update-only, SKIPS (and reports) pairs that do not already exist — the
/// mirror-opposite of batch load.</item>
/// </list>
/// </summary>
public interface IFaturamentoManualEntryService
{
    Task<Faturamento> ManualEntryAsync(
        Guid municipioId,
        string competencia,
        decimal kwhFaturado,
        string? performedBy,
        CancellationToken ct = default);

    Task<FaturamentoBatchResult> BatchLoadAsync(
        IReadOnlyList<FaturamentoBatchItem> items,
        string? performedBy,
        CancellationToken ct = default);

    Task<FaturamentoBatchResult> RecargaAsync(
        IReadOnlyList<FaturamentoBatchItem> items,
        string? performedBy,
        CancellationToken ct = default);
}
