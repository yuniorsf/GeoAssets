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
        var rows = storedLines
            .SelectMany(line => line.OperationGroups.Select(detail =>
                new OficioWorkbookRow(line.CodigoInstalacao, detail.OperationGroup, detail.Count)))
            .ToList();

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
    /// Validates an Ofício order. <b>Does not post to the <c>Faturamento</c> ledger</b> — same
    /// blocker as <c>FiscalizacaoCalculationService.ValidateAsync</c>, investigated as part of
    /// XD01-188: <see cref="AttrAgregado"/>/<see cref="AttrRetroativo"/> are point-count deltas
    /// with no wattage/lamp-type data to derive a real kWh figure from, and the ledger is
    /// kWh-denominated. See that method's doc comment for the full reasoning — it applies here
    /// identically. <see cref="IFaturamentoLedgerService.AccumulateInspectionDeltaAsync"/> is
    /// ready to be called here once a real business-owned point-to-kWh rule is confirmed.
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

        if (!order.Attributes.TryGetValue(AttrAgregado, out var rawAgregado) ||
            !int.TryParse(rawAgregado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalAgregado) ||
            !order.Attributes.TryGetValue(AttrRetroativo, out var rawRetroativo) ||
            !int.TryParse(rawRetroativo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalRetroativo))
        {
            throw new InvalidOperationException(
                $"Order '{serviceOrderId}' is missing a calculated '{AttrAgregado}'/'{AttrRetroativo}' attribute.");
        }

        var now = timeProvider.GetUtcNow();

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
                    $"Validated Ofício (Agregado={totalAgregado}, Retroativo={totalRetroativo}); Faturamento accumulate-posting blocked — no point-to-kWh conversion rule exists (see XD01-188)",
                    IpBillingWorkflowGraph.Validado),
                ct).ConfigureAwait(false);
        }

        return new OficioValidationResult(serviceOrderId, IpBillingWorkflowGraph.Validado, totalAgregado, totalRetroativo);
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
