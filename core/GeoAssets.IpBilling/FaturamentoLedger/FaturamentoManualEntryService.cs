using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>Default implementation of <see cref="IFaturamentoManualEntryService"/>.</summary>
/// <remarks>
/// Each existence check (<see cref="IFaturamentoRepository.GetByMunicipioAndCompetenciaAsync"/>)
/// and the subsequent write are two separate calls, not one atomic operation — a theoretical
/// race exists if two callers target the same (município, competência) pair concurrently. Not
/// guarded against here: this is a low-concurrency, human-triggered legacy-parity admin screen
/// (one operator at a time correcting a billing record), not a hot path, so the added complexity
/// of a database-level constraint/retry loop wasn't judged worth it. If this ever becomes a
/// concurrent/multi-operator surface, revisit.
/// </remarks>
public sealed class FaturamentoManualEntryService(
    IFaturamentoRepository faturamentoRepository,
    IFaturamentoLedgerService ledgerService,
    TimeProvider timeProvider) : IFaturamentoManualEntryService
{
    private const string ReasonAlreadyPosted = "Faturamento já cadastrado";
    private const string ReasonNotFound = "Faturamento inexistente";

    public async Task<Faturamento> ManualEntryAsync(
        Guid municipioId,
        string competencia,
        decimal kwhFaturado,
        string? performedBy,
        CancellationToken ct = default)
    {
        var existing = await faturamentoRepository
            .GetByMunicipioAndCompetenciaAsync(municipioId, competencia, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new FaturamentoAlreadyPostedException(municipioId, competencia);
        }

        var result = await ledgerService.OverwriteManualEntryAsync(
            municipioId, competencia, kwhFaturado, performedBy ?? "system", timeProvider.GetUtcNow(), ct)
            .ConfigureAwait(false);
        await faturamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }

    public Task<FaturamentoBatchResult> BatchLoadAsync(
        IReadOnlyList<FaturamentoBatchItem> items,
        string? performedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return ApplyBatchAsync(items, performedBy, requireExisting: false, skipReason: ReasonAlreadyPosted, ct);
    }

    public Task<FaturamentoBatchResult> RecargaAsync(
        IReadOnlyList<FaturamentoBatchItem> items,
        string? performedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return ApplyBatchAsync(items, performedBy, requireExisting: true, skipReason: ReasonNotFound, ct);
    }

    private async Task<FaturamentoBatchResult> ApplyBatchAsync(
        IReadOnlyList<FaturamentoBatchItem> items,
        string? performedBy,
        bool requireExisting,
        string skipReason,
        CancellationToken ct)
    {
        List<Faturamento> applied = [];
        List<FaturamentoBatchSkip> skipped = [];
        var postedAt = timeProvider.GetUtcNow();

        foreach (var item in items)
        {
            var existing = await faturamentoRepository
                .GetByMunicipioAndCompetenciaAsync(item.MunicipioId, item.Competencia, ct)
                .ConfigureAwait(false);

            var exists = existing is not null;
            if (exists != requireExisting)
            {
                skipped.Add(new FaturamentoBatchSkip(item.MunicipioId, item.Competencia, skipReason));
                continue;
            }

            var result = await ledgerService.OverwriteManualEntryAsync(
                item.MunicipioId, item.Competencia, item.KwhFaturado, performedBy ?? "system", postedAt, ct)
                .ConfigureAwait(false);
            applied.Add(result);
        }

        await faturamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        return new FaturamentoBatchResult(applied, skipped);
    }
}
