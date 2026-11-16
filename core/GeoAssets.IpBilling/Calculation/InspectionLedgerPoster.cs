using System.Globalization;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Default implementation of <see cref="IInspectionLedgerPoster"/> (XD01-301).
///
/// <para><b>Chart resolution.</b> Each <c>(lamp code, wattage)</c> named by a lamp detail is
/// looked up in the município's Distribuidora chart first; when the Distribuidora has no such row
/// — or the município has no Distribuidora — the global standard chart
/// (<see cref="LampType"/>/<see cref="LampWattageOption"/>) is used. The standard watts-per-point
/// for points without lamp detail is the arithmetic mean of <c>TotalBilledW</c> over the
/// Distribuidora chart when it has rows, otherwise over the global standard chart, rounded with
/// <see cref="IpEnergyMath.Round"/>. It is always computed, never hard-coded.</para>
///
/// <para><b>Retroactive period — how it is derived, and the assumption made.</b> A Fiscalização/
/// Ofício line carries no date, so the back-billing window cannot come from the line itself. This
/// implementation anchors it exactly where <c>Levantamento.LevantamentoRetroactiveCalculator</c>
/// anchors its codes 00/01: at the first day of the competência of the município's most recent
/// <em>validated</em> Levantamento strictly before this order's competência — the last moment the
/// billed baseline is known to have been correct. The window is <c>[anchor, first day of this
/// competência)</c>, capped at <see cref="IpEnergyMath.RetroactiveCapDays"/> days
/// (<see cref="InspectionKwhConverter.RetroactiveDays"/>). With no prior validated Levantamento
/// there is nothing to retroact against and the retroactive kWh is zero (the same reasoning as
/// Levantamento's code 30).</para>
///
/// <para>ASSUMPTION (flag for the business owner): the window ends at the <em>first</em> day of
/// the competência, not its last day (Levantamento's default <c>asOf</c>). The monthly figure
/// already bills the whole competência, so ending the retroactive window at its last day would
/// bill those days twice on an accumulate-mode ledger. Which operation groups count as
/// retroactive is not decided here: it follows each flow's own <c>Retroativo</c> formula
/// (Fiscalização: groups 1/2 only; Ofício: <c>OficioLineCalculator.DefaultRetroativoPolicy</c>,
/// the XD01-185 symmetry checkpoint), so resolving that ambiguity stays a one-constant change.</para>
///
/// <para><b>Idempotent retry.</b> When the order already has an Accumulate posting for the
/// competência, no conversion runs — a chart edited after the original posting must not be able to
/// block (or silently change) the retry of an order whose money is already on the ledger.</para>
/// </summary>
public sealed class InspectionLedgerPoster(
    IFaturamentoLedgerService ledgerService,
    IFaturamentoRepository faturamentoRepository,
    IDistribuidoraLampWattageReader distribuidoraChart,
    ILampTypeRepository lampTypeRepository,
    ILevantamentoRepository levantamentoRepository) : IInspectionLedgerPoster
{
    public async Task<InspectionLedgerPosting> PostAsync(
        InspectionLedgerPostingRequest request, CancellationToken ct = default)
    {
        var competenciaStart = EnsureValid(request);
        var municipio = request.Municipio;

        var alreadyPosted = await FindExistingAsync(request, ct).ConfigureAwait(false);
        if (alreadyPosted is not null)
        {
            return alreadyPosted;
        }

        var chart = await ResolveChartAsync(municipio.DistribuidoraId, request.Entries, ct).ConfigureAwait(false);

        var baseline = await levantamentoRepository
            .GetMostRecentValidatedAsync(municipio.Id, request.Competencia, ct).ConfigureAwait(false);
        DateOnly? anchor = baseline is null ? null : ParseCompetenciaStart(baseline.Competencia);

        var conversion = InspectionKwhConverter.Convert(
            request.Entries,
            chart,
            DateTime.DaysInMonth(competenciaStart.Year, competenciaStart.Month),
            InspectionKwhConverter.RetroactiveDays(anchor, competenciaStart));

        var faturamento = await ledgerService.AccumulateInspectionDeltaAsync(
            request.ServiceOrderId,
            municipio.Id,
            request.Competencia,
            conversion.DeltaKwh,
            request.PostedAt,
            conversion.IsProvisional,
            ct).ConfigureAwait(false);
        await faturamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return new InspectionLedgerPosting(
            faturamento,
            conversion.DeltaKwh,
            conversion.IsProvisional,
            conversion.ExactPoints,
            conversion.ProvisionalPoints,
            WasAlreadyPosted: false);
    }

    public async Task<InspectionLedgerPosting?> FindExistingAsync(
        InspectionLedgerPostingRequest request, CancellationToken ct = default)
    {
        EnsureValid(request);

        var postings = await faturamentoRepository
            .GetPostingsByOrderAsync(request.ServiceOrderId, ct).ConfigureAwait(false);
        var posting = postings.FirstOrDefault(p =>
            p.Mode == FaturamentoPostingMode.Accumulate &&
            string.Equals(p.Competencia, request.Competencia, StringComparison.Ordinal));
        if (posting is null)
        {
            return null;
        }

        var ledger = await faturamentoRepository
            .GetByMunicipioAndCompetenciaAsync(request.Municipio.Id, request.Competencia, ct).ConfigureAwait(false);
        if (ledger is null)
        {
            return null;
        }

        return new InspectionLedgerPosting(
            ledger,
            posting.DeltaOrBaselineKwh,
            posting.IsProvisional,
            request.Entries.Where(e => e.HasLampDetail).Sum(e => e.Quantity),
            request.Entries.Where(e => !e.HasLampDetail).Sum(e => e.Quantity),
            WasAlreadyPosted: true);
    }

    private static DateOnly EnsureValid(InspectionLedgerPostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServiceOrderId);
        ArgumentNullException.ThrowIfNull(request.Municipio);
        ArgumentNullException.ThrowIfNull(request.Entries);

        return ParseCompetenciaStart(request.Competencia);
    }

    private async Task<InspectionLampChart> ResolveChartAsync(
        Guid? distribuidoraId, IReadOnlyList<InspectionPointEntry> entries, CancellationToken ct)
    {
        var standardChart = (await lampTypeRepository.GetAllAsync(ct).ConfigureAwait(false))
            .SelectMany(type => type.WattageOptions.Select(option => (type.Code, Option: option)))
            .ToList();

        var resolved = new Dictionary<(LampTypeCode Code, int WattageW), decimal>();
        foreach (var entry in entries)
        {
            if (entry.LampCode is not { } code || entry.WattageW is not { } wattageW || resolved.ContainsKey((code, wattageW)))
            {
                continue;
            }

            var distribuidoraRow = distribuidoraId is { } id
                ? await distribuidoraChart.FindAsync(id, code, wattageW, ct).ConfigureAwait(false)
                : null;
            if (distribuidoraRow is not null)
            {
                resolved[(code, wattageW)] = distribuidoraRow.TotalBilledW;
                continue;
            }

            var standardRow = standardChart.FirstOrDefault(r => r.Code == code && r.Option.WattageW == wattageW).Option;
            if (standardRow is not null)
            {
                resolved[(code, wattageW)] = standardRow.TotalBilledW;
            }
        }

        var mean = distribuidoraId is { } chartOwnerId
            ? await distribuidoraChart.GetMeanTotalBilledWAsync(chartOwnerId, ct).ConfigureAwait(false)
            : null;
        if (mean is null && standardChart.Count > 0)
        {
            mean = standardChart.Average(r => r.Option.TotalBilledW);
        }

        return new InspectionLampChart(resolved, mean is { } value ? IpEnergyMath.Round(value) : null);
    }

    private static DateOnly ParseCompetenciaStart(string competencia) =>
        DateOnly.TryParseExact(
            $"{competencia}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? start
            : throw new ArgumentException($"'{competencia}' is not a valid 'yyyy-MM' competência.", nameof(competencia));
}
