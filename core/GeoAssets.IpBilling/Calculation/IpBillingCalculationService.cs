using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.AssetTypes;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>Default implementation of <see cref="IIpBillingCalculationService"/>.</summary>
public sealed class IpBillingCalculationService(
    IServiceOrderRepository orderRepository,
    IMunicipioRepository municipioRepository,
    ILampTypeRepository lampTypeRepository,
    IFaturamentoRepository faturamentoRepository,
    ITransformerResolver transformerResolver,
    IPoleResolver poleResolver,
    IGeoCoreClient geoCoreClient,
    TimeProvider timeProvider) : IIpBillingCalculationService
{
    public const string AttrMunicipioIbgeCode = "municipioIbgeCode";
    public const string AttrCompetencia       = "competencia";
    public const string AttrCodigoInstalacao  = "codigoInstalacao";
    public const string AttrCalculatedKwh     = "calculatedKwh";
    public const string AttrNormalKwh         = "normalKwh";
    public const string AttrIrregular24hKwh   = "irregular24hKwh";
    public const string AttrResolvedPoleCount = "resolvedPoleCount";

    public async Task<IpBillingCalculationResult> CalculateOrderAsync(
        string orderId,
        IpBillingCalculationRequest? request = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var order = await LoadMutableOrderAsync(orderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, IpBillingWorkflowGraph.Carregado, StringComparison.Ordinal))
        {
            throw new InvalidServiceOrderTransitionException(order.Status, IpBillingWorkflowGraph.Calculado);
        }

        var (ibgeCode, competencia, daysInMonth, defaultAsOf) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        var lampCatalog = await lampTypeRepository.GetAllAsync(ct).ConfigureAwait(false);
        var poles       = await ResolvePolesAsync(order, ibgeCode, request?.CodigoInstalacao, ct).ConfigureAwait(false);

        var normalKwh = ComputeNormalLampsKwh(request?.NormalLamps ?? [], lampCatalog, daysInMonth);
        var irregular24hKwh = await ComputeIrregular24hKwhAsync(
            ibgeCode,
            lampCatalog,
            daysInMonth,
            request?.ReferenceDate ?? defaultAsOf,
            ct).ConfigureAwait(false);

        var totalKwh = IpEnergyMath.Round(normalKwh + irregular24hKwh);

        StampCalculationAttributes(order, normalKwh, irregular24hKwh, totalKwh, poles.Count);
        await orderRepository.UpdateAsync(order, ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await orderRepository.AppendActionAsync(
            orderId,
            new OrderActionLog(
                OrderActionType.Calculate,
                request?.PerformedBy ?? "system",
                now,
                $"Calculated {totalKwh.ToString("F4", CultureInfo.InvariantCulture)} kWh",
                IpBillingWorkflowGraph.Calculado),
            ct).ConfigureAwait(false);

        return new IpBillingCalculationResult(
            orderId,
            IpBillingWorkflowGraph.Calculado,
            municipio.Id,
            competencia,
            normalKwh,
            irregular24hKwh,
            totalKwh,
            poles.Count,
            poles);
    }

    public async Task<IpBillingValidationResult> ValidateOrderAsync(
        string orderId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var order = await LoadMutableOrderAsync(orderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal) &&
            !string.Equals(order.Status, IpBillingWorkflowGraph.Validado, StringComparison.Ordinal))
        {
            throw new InvalidServiceOrderTransitionException(order.Status, IpBillingWorkflowGraph.Validado);
        }

        var (ibgeCode, competencia, _, _) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        if (!order.Attributes.TryGetValue(AttrCalculatedKwh, out var rawKwh) ||
            !decimal.TryParse(rawKwh, NumberStyles.Number, CultureInfo.InvariantCulture, out var calculatedKwh))
        {
            throw new InvalidOperationException($"Order '{orderId}' does not have a valid '{AttrCalculatedKwh}' attribute.");
        }

        var mode = IpBillingOrderTypes.PostingModeFor(order.OrderTypeId);
        var now  = timeProvider.GetUtcNow();

        var faturamento = await faturamentoRepository.PostOrderCalculationAsync(
            orderId,
            municipio.Id,
            competencia,
            calculatedKwh,
            mode,
            now,
            ct).ConfigureAwait(false);
        await faturamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        if (string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal))
        {
            await orderRepository.AppendActionAsync(
                orderId,
                new OrderActionLog(
                    OrderActionType.Validate,
                    performedBy ?? "system",
                    now.UtcDateTime,
                    $"Validated and posted ({mode}) to Faturamento {competencia}",
                    IpBillingWorkflowGraph.Validado),
                ct).ConfigureAwait(false);
        }

        return new IpBillingValidationResult(
            orderId,
            IpBillingWorkflowGraph.Validado,
            faturamento,
            mode);
    }

    private async Task<ServiceOrder> LoadMutableOrderAsync(string orderId, CancellationToken ct)
    {
        var loaded = await orderRepository.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        return loaded as ServiceOrder
            ?? throw new InvalidOperationException($"Expected mutable {nameof(ServiceOrder)}.");
    }

    private static (string IbgeCode, string Competencia, int DaysInMonth, DateOnly DefaultAsOf) ReadOrderBillingMetadata(
        ServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(AttrMunicipioIbgeCode, out var ibgeCode) ||
            string.IsNullOrWhiteSpace(ibgeCode))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing required attribute '{AttrMunicipioIbgeCode}'.");
        }

        if (!order.Attributes.TryGetValue(AttrCompetencia, out var competencia) ||
            !DateOnly.TryParseExact($"{competencia}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDay))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing a valid 'yyyy-MM' attribute '{AttrCompetencia}'.");
        }

        var daysInMonth = DateTime.DaysInMonth(firstDay.Year, firstDay.Month);
        var lastDay     = new DateOnly(firstDay.Year, firstDay.Month, daysInMonth);
        return (ibgeCode, competencia, daysInMonth, lastDay);
    }

    private async Task<IReadOnlyList<Guid>> ResolvePolesAsync(
        ServiceOrder order,
        string ibgeCode,
        string? requestCodigoInstalacao,
        CancellationToken ct)
    {
        var codigo = requestCodigoInstalacao;
        if (string.IsNullOrWhiteSpace(codigo))
        {
            order.Attributes.TryGetValue(AttrCodigoInstalacao, out codigo);
        }

        if (string.IsNullOrWhiteSpace(codigo))
        {
            return [];
        }

        var transformerFeatureId = await transformerResolver
            .ResolveTransformerFeatureIdAsync(ibgeCode, codigo, ct)
            .ConfigureAwait(false);

        if (transformerFeatureId is null)
        {
            return [];
        }

        return await poleResolver
            .ResolvePoleFeatureIdsAsync(transformerFeatureId.Value, ct)
            .ConfigureAwait(false);
    }

    private static decimal ComputeNormalLampsKwh(
        IReadOnlyList<IpLampLineItem> items,
        IReadOnlyList<LampType> catalog,
        int daysInMonth)
    {
        decimal total = 0m;
        foreach (var item in items)
        {
            if (item.Quantity <= 0) continue;
            var billedWatts = ResolveTotalBilledWatts(catalog, item.Code, item.WattageW);
            var itemKwh     = item.Quantity * (billedWatts / 1000m) * IpEnergyMath.NormalLampDailyHours * daysInMonth;
            total += itemKwh;
        }

        return IpEnergyMath.Round(total);
    }

    private async Task<decimal> ComputeIrregular24hKwhAsync(
        string ibgeCode,
        IReadOnlyList<LampType> catalog,
        int daysInMonth,
        DateOnly asOf,
        CancellationToken ct)
    {
        var page = await geoCoreClient.QueryFeaturesAsync(
            new AssetQuery
            {
                AssetTypeId = IpBillingAssetTypes.LampGroup24h.Id.ToString(),
                AttributeFilters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["scopeId"] = ibgeCode,
                },
                Take = 10_000,
            },
            ct).ConfigureAwait(false);

        decimal total = 0m;
        foreach (var feature in page.Items)
        {
            total += EvaluateLampGroup24hFeature(feature, catalog, daysInMonth, asOf);
        }

        return IpEnergyMath.Round(total);
    }

    private static decimal EvaluateLampGroup24hFeature(
        GeoFeature feature,
        IReadOnlyList<LampType> catalog,
        int daysInMonth,
        DateOnly asOf)
    {
        var attrs = feature.Properties.CustomAttributes;
        if (attrs.TryGetValue("eventDate", out var rawDate) &&
            DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eventDate) &&
            !IpEnergyMath.IsWithinRetroactiveCap(asOf, eventDate))
        {
            return 0m;
        }

        if (!attrs.TryGetValue("lampType", out var rawCode) ||
            !Enum.TryParse<LampTypeCode>(rawCode, ignoreCase: true, out var code) ||
            !attrs.TryGetValue("wattageW", out var rawWattage) ||
            !int.TryParse(rawWattage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wattageW))
        {
            return 0m;
        }

        var quantity = 1;
        if (attrs.TryGetValue("quantity", out var rawQty) &&
            int.TryParse(rawQty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQty))
        {
            quantity = parsedQty;
        }

        if (quantity <= 0) return 0m;

        var billedWatts = ResolveTotalBilledWatts(catalog, code, wattageW);
        return quantity * (billedWatts / 1000m) * IpEnergyMath.Irregular24hDailyHours * daysInMonth;
    }

    private static decimal ResolveTotalBilledWatts(
        IReadOnlyList<LampType> catalog,
        LampTypeCode code,
        int wattageW)
    {
        var lampType = catalog.FirstOrDefault(t => t.Code == code);
        var option   = lampType?.WattageOptions.FirstOrDefault(w => w.WattageW == wattageW);
        return option?.TotalBilledW ?? wattageW;
    }

    private static void StampCalculationAttributes(
        ServiceOrder order,
        decimal normalKwh,
        decimal irregular24hKwh,
        decimal totalKwh,
        int resolvedPoleCount)
    {
        order.Attributes[AttrNormalKwh]         = normalKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[AttrIrregular24hKwh]   = irregular24hKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[AttrCalculatedKwh]     = totalKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[AttrResolvedPoleCount] = resolvedPoleCount.ToString(CultureInfo.InvariantCulture);
    }
}
