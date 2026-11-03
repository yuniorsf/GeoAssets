using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>Default implementation of <see cref="IFiscalizacaoCalculationService"/>.</summary>
public sealed class FiscalizacaoCalculationService(
    IServiceOrderRepository orderRepository,
    IMunicipioRepository municipioRepository,
    IFiscalizacaoRepository fiscalizacaoRepository,
    TimeProvider timeProvider) : IFiscalizacaoCalculationService
{
    public const string AttrAgregado = "fiscalizacaoAgregado";
    public const string AttrRetroativo = "fiscalizacaoRetroativo";

    public async Task<int> UploadAsync(
        string serviceOrderId,
        IReadOnlyList<FiscalizacaoWorkbookRow> rows,
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

        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, ct).ConfigureAwait(false);

        var lines = FiscalizacaoWorkbookMapper.ToLines(serviceOrderId, rows);
        await fiscalizacaoRepository.ReplaceLinesAsync(serviceOrderId, lines, ct).ConfigureAwait(false);
        await fiscalizacaoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return lines.Count;
    }

    public async Task<FiscalizacaoCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);

        var storedLines = await fiscalizacaoRepository.GetLinesAsync(serviceOrderId, ct).ConfigureAwait(false);
        var rows = storedLines
            .SelectMany(line => line.OperationGroups.Select(detail =>
                new FiscalizacaoWorkbookRow(line.CodigoInstalacao, detail.OperationGroup, detail.Count)))
            .ToList();

        return await CalculateAsync(serviceOrderId, rows, performedBy, ct).ConfigureAwait(false);
    }

    public async Task<FiscalizacaoCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<FiscalizacaoWorkbookRow> rows,
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

        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, ct).ConfigureAwait(false);

        var lines = FiscalizacaoWorkbookMapper.ToLines(serviceOrderId, rows);
        foreach (var line in lines)
        {
            FiscalizacaoLineCalculator.Recalculate(line);
        }

        await fiscalizacaoRepository.ReplaceLinesAsync(serviceOrderId, lines, ct).ConfigureAwait(false);
        await fiscalizacaoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return await FinalizeCalculationAsync(order, serviceOrderId, municipio.Id, competencia, lines, performedBy, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a Fiscalização order. <b>Does not post to the <c>Faturamento</c> ledger</b> —
    /// investigated as part of XD01-188 (the ledger write-path ticket) and found to be a real
    /// blocker, not an oversight: <see cref="AttrAgregado"/>/<see cref="AttrRetroativo"/> are
    /// point-count deltas (see <see cref="FiscalizacaoLineCalculator"/> and XD01-182's own
    /// scoping note, "a point-count reconciliation, not kWh — no lamp/wattage/transformer
    /// -resolution complexity"), and the ledger (<see cref="Models.Faturamento.KwhFaturado"/>)
    /// is denominated in kWh. There is no wattage/lamp-type data anywhere on a Fiscalização
    /// line to derive a real kWh figure from a point count — this is a missing INPUT, not a
    /// missing conversion CONSTANT, so XD01-188 deliberately did not invent one (multiplying an
    /// unrelated point count by any made-up rate would post a fabricated number to a real
    /// financial ledger). <see cref="IFaturamentoLedgerService.AccumulateInspectionDeltaAsync"/>
    /// exists, is implemented, and is tested — it is ready to be called here the moment a real
    /// business-owned point-to-kWh rule (or a redesign giving Fiscalização real wattage data)
    /// is confirmed. Flagging for a human decision rather than silently leaving this unwired
    /// forever or guessing a number.
    /// </summary>
    public async Task<FiscalizacaoValidationResult> ValidateAsync(
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

        await fiscalizacaoRepository.MarkValidatedAsync(serviceOrderId, ct).ConfigureAwait(false);
        await fiscalizacaoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        if (string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal))
        {
            await orderRepository.AppendActionAsync(
                serviceOrderId,
                new OrderActionLog(
                    OrderActionType.Validate,
                    performedBy ?? "system",
                    now.UtcDateTime,
                    $"Validated Fiscalização (Agregado={totalAgregado}, Retroativo={totalRetroativo}); Faturamento accumulate-posting blocked — no point-to-kWh conversion rule exists (see XD01-188)",
                    IpBillingWorkflowGraph.Validado),
                ct).ConfigureAwait(false);
        }

        return new FiscalizacaoValidationResult(serviceOrderId, IpBillingWorkflowGraph.Validado, totalAgregado, totalRetroativo);
    }

    private async Task EnsureHeaderExistsAsync(
        string serviceOrderId, Guid municipioId, string competencia, CancellationToken ct)
    {
        if (await fiscalizacaoRepository.GetHeaderAsync(serviceOrderId, ct).ConfigureAwait(false) is not null) return;

        await fiscalizacaoRepository.AddHeaderAsync(
            new FiscalizacaoHeader
            {
                ServiceOrderId = serviceOrderId,
                MunicipioId = municipioId,
                Competencia = competencia,
            },
            ct).ConfigureAwait(false);
    }

    private async Task<FiscalizacaoCalculationResult> FinalizeCalculationAsync(
        ServiceOrder order,
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        List<FiscalizacaoLine> lines,
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

        return new FiscalizacaoCalculationResult(
            serviceOrderId, IpBillingWorkflowGraph.Calculado, municipioId, competencia, totalAgregado, totalRetroativo, lines);
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
