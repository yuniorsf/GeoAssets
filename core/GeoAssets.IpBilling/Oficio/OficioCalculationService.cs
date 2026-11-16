using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Oficio;

/// <summary>Default implementation of <see cref="IOficioCalculationService"/>.</summary>
public sealed class OficioCalculationService(
    IServiceOrderRepository orderRepository,
    IMunicipioRepository municipioRepository,
    IOficioRepository oficioRepository,
    IInspectionLedgerPoster ledgerPoster,
    TimeProvider timeProvider) : IOficioCalculationService
{
    public const string AttrAgregado = "oficioAgregado";
    public const string AttrRetroativo = "oficioRetroativo";

    public async Task<int> UploadAsync(
        string serviceOrderId,
        string? numeroPrefeitura,
        IReadOnlyList<OficioWorkbookRow> rows,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(rows);

        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, IpBillingWorkflowGraph.Carregado, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot upload to order '{serviceOrderId}' — status is '{order.Status}', expected '{IpBillingWorkflowGraph.Carregado}'.");
        }

        var (ibgeCode, competencia) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, numeroPrefeitura, ct).ConfigureAwait(false);

        var lines = OficioWorkbookMapper.ToLines(serviceOrderId, rows);
        await oficioRepository.ReplaceLinesAsync(serviceOrderId, lines, ct).ConfigureAwait(false);
        await oficioRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return lines.Count;
    }

    public async Task<OficioCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);

        var storedLines = await oficioRepository.GetLinesAsync(serviceOrderId, ct).ConfigureAwait(false);
        var rows = OficioWorkbookMapper.ToRows(storedLines);

        return await CalculateAsync(serviceOrderId, rows, performedBy, ct).ConfigureAwait(false);
    }

    public async Task<OficioCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<OficioWorkbookRow> rows,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(rows);

        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, IpBillingWorkflowGraph.Carregado, StringComparison.Ordinal) &&
            !string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal))
        {
            throw new InvalidServiceOrderTransitionException(order.Status, IpBillingWorkflowGraph.Calculado);
        }

        var (ibgeCode, competencia) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        // Calculate may run without a prior UploadAsync (defensive, mirrors Fiscalização) — in
        // that case no número is known yet, so none is claimed here. The normal flow claims the
        // número during UploadAsync, which runs the duplicate-guard check.
        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, numeroPrefeitura: null, ct).ConfigureAwait(false);

        var lines = OficioWorkbookMapper.ToLines(serviceOrderId, rows);
        foreach (var line in lines)
        {
            OficioLineCalculator.Recalculate(line);
        }

        await oficioRepository.ReplaceLinesAsync(serviceOrderId, lines, ct).ConfigureAwait(false);
        await oficioRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        var header = await oficioRepository.GetHeaderAsync(serviceOrderId, ct).ConfigureAwait(false);
        var swaps = OficioLineSwapView.Compute(lines);

        return await FinalizeCalculationAsync(
            order, serviceOrderId, municipio.Id, competencia, header?.NumeroPrefeitura, lines, swaps, performedBy, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates an Ofício order and ACCUMULATEs its kWh delta onto the <c>Faturamento</c>
    /// ledger (XD01-301, unblocking what XD01-188 had to leave unwired). The order's persisted
    /// lines are normalized by <see cref="OficioKwhEntryMapper"/> and converted by
    /// <see cref="InspectionKwhConverter"/> through <see cref="IInspectionLedgerPoster"/>: points
    /// with lamp detail convert exactly, points known only as a count convert provisionally with
    /// the applicable chart's mean watts-per-point, and a posting containing any provisional
    /// point is marked <c>FaturamentoPosting.IsProvisional</c> for later recalculation.
    ///
    /// <para>Ordering mirrors <c>LevantamentoCalculationService.ValidateAsync</c> (XD01-188): the
    /// ledger write commits FIRST, then the header is marked validated, then the order
    /// transitions. A crash after the ledger write leaves the order <c>Calculado</c>; retrying is
    /// safe because the posting is idempotent per <c>(serviceOrderId, competencia)</c> and the
    /// retry completes the transition without posting again. Re-validating an order that is
    /// already <c>Validado</c> never posts: it only reports the posting it made, if any (an
    /// order validated before XD01-301 has none, and <c>Faturamento</c> is then <c>null</c> on
    /// the result). Any conversion failure (e.g.
    /// <see cref="InspectionLampNotInChartException"/>) throws before anything is written.</para>
    /// </summary>
    public async Task<OficioValidationResult> ValidateAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);

        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal) &&
            !string.Equals(order.Status, IpBillingWorkflowGraph.Validado, StringComparison.Ordinal))
        {
            throw new InvalidServiceOrderTransitionException(order.Status, IpBillingWorkflowGraph.Validado);
        }

        var (totalAgregado, totalRetroativo) = ReadCalculatedTotals(order);
        var (ibgeCode, competencia) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        var lines = await oficioRepository.GetLinesAsync(serviceOrderId, ct).ConfigureAwait(false);
        var entries = OficioKwhEntryMapper.ToEntries(lines);

        var now = timeProvider.GetUtcNow();

        var request = new InspectionLedgerPostingRequest(serviceOrderId, municipio, competencia, entries, now);

        // Ledger first, order transition last — see this method's doc comment. An order that is
        // already Validado only reports what it posted; it never posts anew.
        var posting = string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal)
            ? await ledgerPoster.PostAsync(request, ct).ConfigureAwait(false)
            : await ledgerPoster.FindExistingAsync(request, ct).ConfigureAwait(false);

        await oficioRepository.MarkValidatedAsync(serviceOrderId, ct).ConfigureAwait(false);
        await oficioRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        if (string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal))
        {
            await orderRepository.AppendActionAsync(
                serviceOrderId,
                new OrderActionLog(
                    OrderActionType.Validate,
                    performedBy ?? "system",
                    now.UtcDateTime,
                    $"Validated Ofício (Agregado={totalAgregado}, Retroativo={totalRetroativo}); {DescribePosting(posting, competencia)}",
                    IpBillingWorkflowGraph.Validado),
                ct).ConfigureAwait(false);
        }

        return new OficioValidationResult(
            serviceOrderId,
            IpBillingWorkflowGraph.Validado,
            totalAgregado,
            totalRetroativo,
            posting?.Faturamento,
            posting?.DeltaKwh ?? 0m,
            posting?.IsProvisional ?? false,
            posting?.ExactPoints ?? 0,
            posting?.ProvisionalPoints ?? 0);
    }

    private static (int TotalAgregado, int TotalRetroativo) ReadCalculatedTotals(ServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(AttrAgregado, out var rawAgregado) ||
            !int.TryParse(rawAgregado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalAgregado) ||
            !order.Attributes.TryGetValue(AttrRetroativo, out var rawRetroativo) ||
            !int.TryParse(rawRetroativo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalRetroativo))
        {
            throw new InvalidOperationException(
                $"Order '{order.Id}' is missing a calculated '{AttrAgregado}'/'{AttrRetroativo}' attribute.");
        }

        return (totalAgregado, totalRetroativo);
    }

    private static string DescribePosting(InspectionLedgerPosting? posting, string competencia)
    {
        if (posting is null)
        {
            return "no Faturamento posting";
        }

        var kwh = posting.DeltaKwh.ToString("F4", CultureInfo.InvariantCulture);
        var kind = posting.IsProvisional
            ? $"PROVISIONAL conversion — {posting.ProvisionalPoints} point(s) without lamp detail used the chart's mean watts-per-point, pending recalculation"
            : "exact conversion from lamp detail";
        return $"posted (Accumulate) {kwh} kWh to Faturamento {competencia} — {kind}";
    }

    public async Task SupersedeAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);

        var header = await oficioRepository.GetHeaderAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Ofício header for order '{serviceOrderId}' not found.");

        var previousNumero = header.NumeroPrefeitura
            ?? throw new InvalidOperationException(
                $"Order '{serviceOrderId}' has no NumeroPrefeitura to supersede.");

        await oficioRepository.ClearNumeroPrefeituraAsync(serviceOrderId, ct).ConfigureAwait(false);
        await oficioRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Annotate,
                performedBy ?? "system",
                now,
                $"Ofício número '{previousNumero}' superseded — released for reassignment to a corrective upload."),
            ct).ConfigureAwait(false);
    }

    private async Task EnsureHeaderExistsAsync(
        string serviceOrderId, Guid municipioId, string competencia, string? numeroPrefeitura, CancellationToken ct)
    {
        if (await oficioRepository.GetHeaderAsync(serviceOrderId, ct).ConfigureAwait(false) is not null) return;

        if (!string.IsNullOrWhiteSpace(numeroPrefeitura))
        {
            var existing = await oficioRepository.GetHeaderByNumeroAsync(municipioId, numeroPrefeitura, ct).ConfigureAwait(false);
            if (existing is not null && !string.Equals(existing.ServiceOrderId, serviceOrderId, StringComparison.Ordinal))
            {
                throw new DuplicateOficioNumeroException(municipioId, numeroPrefeitura, existing.ServiceOrderId);
            }
        }

        await oficioRepository.AddHeaderAsync(
            new OficioHeader
            {
                ServiceOrderId = serviceOrderId,
                MunicipioId = municipioId,
                Competencia = competencia,
                NumeroPrefeitura = numeroPrefeitura,
            },
            ct).ConfigureAwait(false);
    }

    private async Task<OficioCalculationResult> FinalizeCalculationAsync(
        ServiceOrder order,
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        string? numeroPrefeitura,
        List<OficioLine> lines,
        IReadOnlyList<OficioLineSwapResult> swaps,
        string? performedBy,
        CancellationToken ct)
    {
        var totalAgregado = lines.Sum(l => l.Agregado);
        var totalRetroativo = lines.Sum(l => l.Retroativo);

        order.Attributes[AttrAgregado] = totalAgregado.ToString(CultureInfo.InvariantCulture);
        order.Attributes[AttrRetroativo] = totalRetroativo.ToString(CultureInfo.InvariantCulture);
        await orderRepository.UpdateAsync(order, ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Calculate,
                performedBy ?? "system",
                now,
                $"Calculated Agregado={totalAgregado}, Retroativo={totalRetroativo} across {lines.Count} line(s)",
                IpBillingWorkflowGraph.Calculado),
            ct).ConfigureAwait(false);

        return new OficioCalculationResult(
            serviceOrderId, IpBillingWorkflowGraph.Calculado, municipioId, competencia, numeroPrefeitura,
            totalAgregado, totalRetroativo, lines, swaps);
    }

    private async Task<ServiceOrder> LoadMutableOrderAsync(string serviceOrderId, CancellationToken ct)
    {
        var loaded = await orderRepository.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{serviceOrderId}' not found.");

        return loaded as ServiceOrder
            ?? throw new InvalidOperationException($"Expected mutable {nameof(ServiceOrder)}.");
    }

    private static (string IbgeCode, string Competencia) ReadOrderBillingMetadata(ServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(IpBillingCalculationService.AttrMunicipioIbgeCode, out var ibgeCode) ||
            string.IsNullOrWhiteSpace(ibgeCode))
        {
            throw new InvalidOperationException(
                $"Order '{order.Id}' is missing required attribute '{IpBillingCalculationService.AttrMunicipioIbgeCode}'.");
        }

        if (!order.Attributes.TryGetValue(IpBillingCalculationService.AttrCompetencia, out var competencia) ||
            !DateOnly.TryParseExact($"{competencia}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException(
                $"Order '{order.Id}' is missing a valid 'yyyy-MM' attribute '{IpBillingCalculationService.AttrCompetencia}'.");
        }

        return (ibgeCode, competencia);
    }
}
