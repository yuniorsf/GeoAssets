using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.AssetTypes;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>Default implementation of <see cref="ILevantamentoCalculationService"/>.</summary>
public sealed class LevantamentoCalculationService(
    IServiceOrderRepository orderRepository,
    IMunicipioRepository municipioRepository,
    ILampTypeRepository lampTypeRepository,
    ILevantamentoRepository levantamentoRepository,
    ITransformerResolver transformerResolver,
    IInterveningOperationLookup interveningLookup,
    IFaturamentoRepository faturamentoRepository,
    IGeoCoreClient geoCoreClient,
    TimeProvider timeProvider) : ILevantamentoCalculationService
{
    public const string AttrRetroativoKwh = "retroativoKwh";

    public async Task<int> UploadAsync(
        string serviceOrderId,
        IReadOnlyList<LevantamentoWorkbookRow> rows,
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

        var (ibgeCode, competencia, _, _, _) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, ct).ConfigureAwait(false);

        var transformerLines = LevantamentoWorkbookMapper.ToTransformerLines(serviceOrderId, rows);
        await levantamentoRepository.ReplaceTransformerLinesAsync(serviceOrderId, transformerLines, ct).ConfigureAwait(false);
        await levantamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return transformerLines.Count;
    }

    public async Task<LevantamentoCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        DateOnly? asOf = null,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);

        var storedLines = await levantamentoRepository.GetTransformerLinesAsync(serviceOrderId, ct).ConfigureAwait(false);
        var rows = storedLines
            .SelectMany(line => line.LampDetails.Select(detail =>
                new LevantamentoWorkbookRow(line.CodigoInstalacao, detail.Code, detail.WattageW, detail.Quantity)))
            .ToList();

        return await CalculateAsync(serviceOrderId, rows, asOf, performedBy, ct).ConfigureAwait(false);
    }

    public async Task<LevantamentoCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<LevantamentoWorkbookRow> rows,
        DateOnly? asOf = null,
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

        var (ibgeCode, competencia, daysInMonth, competenciaStart, defaultAsOf) = ReadOrderBillingMetadata(order);
        var effectiveAsOf = asOf ?? defaultAsOf;

        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        await EnsureHeaderExistsAsync(serviceOrderId, municipio.Id, competencia, ct).ConfigureAwait(false);

        var lampCatalog = await lampTypeRepository.GetAllAsync(ct).ConfigureAwait(false);
        var transformerLines = LevantamentoWorkbookMapper.ToTransformerLines(serviceOrderId, rows);

        var snapshot = await BuildHistorySnapshotAsync(municipio.Id, competencia, ct).ConfigureAwait(false);
        var unresolved = await ProcessTransformerLinesAsync(
            transformerLines, ibgeCode, lampCatalog, daysInMonth, effectiveAsOf, municipio.Id, snapshot, ct)
            .ConfigureAwait(false);

        await levantamentoRepository.ReplaceTransformerLinesAsync(serviceOrderId, transformerLines, ct).ConfigureAwait(false);

        var lines24h = await ComputeIrregular24hLinesAsync(serviceOrderId, ibgeCode, lampCatalog, daysInMonth, effectiveAsOf, ct)
            .ConfigureAwait(false);
        await levantamentoRepository.Replace24hLinesAsync(serviceOrderId, lines24h, ct).ConfigureAwait(false);
        await levantamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return await FinalizeCalculationAsync(
            order, serviceOrderId, municipio.Id, competencia, transformerLines, lines24h, unresolved, performedBy, ct)
            .ConfigureAwait(false);
    }

    private async Task EnsureHeaderExistsAsync(
        string serviceOrderId, Guid municipioId, string competencia, CancellationToken ct)
    {
        if (await levantamentoRepository.GetHeaderAsync(serviceOrderId, ct).ConfigureAwait(false) is not null) return;

        await levantamentoRepository.AddHeaderAsync(
            new LevantamentoHeader
            {
                ServiceOrderId = serviceOrderId,
                MunicipioId = municipioId,
                Competencia = competencia,
            },
            ct).ConfigureAwait(false);
    }

    private async Task<LevantamentoCalculationResult> FinalizeCalculationAsync(
        ServiceOrder order,
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        List<LevantamentoTransformerLine> transformerLines,
        List<Levantamento24hLine> lines24h,
        List<string> unresolved,
        string? performedBy,
        CancellationToken ct)
    {
        var normalKwh = IpEnergyMath.Round(transformerLines.Sum(l => l.NormalKwh));
        var retroativoKwh = IpEnergyMath.Round(transformerLines.Sum(l => l.RetroativoKwh ?? 0m));
        var irregular24hKwh = IpEnergyMath.Round(lines24h.Sum(l => l.Kwh));
        var totalKwh = IpEnergyMath.Round(normalKwh + retroativoKwh + irregular24hKwh);

        StampCalculationAttributes(order, normalKwh, retroativoKwh, irregular24hKwh, totalKwh);
        await orderRepository.UpdateAsync(order, ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Calculate,
                performedBy ?? "system",
                now,
                $"Calculated {totalKwh.ToString("F4", CultureInfo.InvariantCulture)} kWh across {transformerLines.Count} transformer(s)",
                IpBillingWorkflowGraph.Calculado),
            ct).ConfigureAwait(false);

        return new LevantamentoCalculationResult(
            serviceOrderId,
            IpBillingWorkflowGraph.Calculado,
            municipioId,
            competencia,
            normalKwh,
            retroativoKwh,
            irregular24hKwh,
            totalKwh,
            transformerLines,
            lines24h,
            unresolved);
    }

    private sealed record MunicipioHistorySnapshot(
        LevantamentoHeader? MostRecentValidated,
        LevantamentoHeader? FirstValidated,
        IReadOnlySet<string> AllValidatedCodigos,
        IReadOnlySet<string> CodigosInMostRecentValidated);

    /// <summary>Município-wide history, fetched once per <see cref="CalculateAsync"/> call and
    /// reused for every transformer line in the upload rather than re-querying per line.</summary>
    private async Task<MunicipioHistorySnapshot> BuildHistorySnapshotAsync(
        Guid municipioId, string competencia, CancellationToken ct)
    {
        var mostRecentValidated = await levantamentoRepository
            .GetMostRecentValidatedAsync(municipioId, competencia, ct).ConfigureAwait(false);
        var firstValidated = await levantamentoRepository
            .GetFirstValidatedAsync(municipioId, ct).ConfigureAwait(false);
        var allValidatedCodigos = await levantamentoRepository
            .GetAllValidatedCodigosInstalacaoAsync(municipioId, ct).ConfigureAwait(false);

        var codigosInMostRecentValidated = mostRecentValidated is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await levantamentoRepository.GetTransformerLinesAsync(mostRecentValidated.ServiceOrderId, ct).ConfigureAwait(false))
                .Select(l => l.CodigoInstalacao)
                .ToHashSet(StringComparer.Ordinal);

        return new MunicipioHistorySnapshot(
            mostRecentValidated, firstValidated, allValidatedCodigos, codigosInMostRecentValidated);
    }

    /// <summary>Resolves, rates, and determines the <see cref="TipoRetroacao"/> outcome for every
    /// line, mutating each in place. Returns the códigos de instalação that failed resolution —
    /// reported together per this ticket's "not one at a time" requirement.</summary>
    private async Task<List<string>> ProcessTransformerLinesAsync(
        List<LevantamentoTransformerLine> transformerLines,
        string ibgeCode,
        IReadOnlyList<LampType> lampCatalog,
        int daysInMonth,
        DateOnly effectiveAsOf,
        Guid municipioId,
        MunicipioHistorySnapshot snapshot,
        CancellationToken ct)
    {
        var unresolved = new List<string>();

        foreach (var line in transformerLines)
        {
            line.TransformerFeatureId = await transformerResolver
                .ResolveTransformerFeatureIdAsync(ibgeCode, line.CodigoInstalacao, ct).ConfigureAwait(false);
            if (line.TransformerFeatureId is null)
            {
                unresolved.Add(line.CodigoInstalacao);
            }

            var dailyRate = ComputeDailyNormalKwhRate(line.LampDetails, lampCatalog);
            line.NormalKwh = IpEnergyMath.Round(dailyRate * daysInMonth);

            var history = await BuildTransformerHistoryContextAsync(line.CodigoInstalacao, effectiveAsOf, municipioId, snapshot, ct)
                .ConfigureAwait(false);

            var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(history, effectiveAsOf, dailyRate);
            line.IsNewTransformer = outcome.IsNewTransformer;
            line.Tipo = outcome.Tipo;
            line.RetroativoKwh = outcome.RetroativoKwh;
        }

        return unresolved;
    }

    private async Task<TransformerHistoryContext> BuildTransformerHistoryContextAsync(
        string codigoInstalacao,
        DateOnly effectiveAsOf,
        Guid municipioId,
        MunicipioHistorySnapshot snapshot,
        CancellationToken ct)
    {
        var seenInMostRecent = snapshot.CodigosInMostRecentValidated.Contains(codigoInstalacao, StringComparer.Ordinal);

        // Only meaningful for a transformer that will fall into the "reappeared" branch
        // (not seen in the most recent survey, but município history exists) — skip the lookup
        // otherwise.
        InterveningOperationCoverage? coverage = snapshot.FirstValidated is not null && !seenInMostRecent
            ? await interveningLookup.GetMostRecentValidatedAsync(municipioId, codigoInstalacao, effectiveAsOf, ct).ConfigureAwait(false)
            : null;

        return new TransformerHistoryContext(
            MunicipioHasAnyValidatedLevantamento: snapshot.FirstValidated is not null,
            SeenInMostRecentValidatedLevantamento: seenInMostRecent,
            EverSeenInAnyValidatedLevantamento: snapshot.AllValidatedCodigos.Contains(codigoInstalacao, StringComparer.Ordinal),
            MostRecentValidatedLevantamentoCompetenciaStart: snapshot.MostRecentValidated is null
                ? null : ParseCompetenciaStart(snapshot.MostRecentValidated.Competencia),
            FirstValidatedLevantamentoCompetenciaStart: snapshot.FirstValidated is null
                ? null : ParseCompetenciaStart(snapshot.FirstValidated.Competencia),
            FiscalizacaoAnchorDate: coverage?.ValidatedThrough,
            AlreadyBilledKwhWithinWindow: coverage?.AlreadyBilledKwh ?? 0m);
    }

    public async Task<LevantamentoValidationResult> ValidateAsync(
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

        var (ibgeCode, competencia, _, _, _) = ReadOrderBillingMetadata(order);
        var municipio = await municipioRepository.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Município with IBGE code '{ibgeCode}' not found.");

        if (!order.Attributes.TryGetValue(IpBillingCalculationService.AttrCalculatedKwh, out var rawKwh) ||
            !decimal.TryParse(rawKwh, NumberStyles.Number, CultureInfo.InvariantCulture, out var calculatedKwh))
        {
            throw new InvalidOperationException(
                $"Order '{serviceOrderId}' does not have a valid '{IpBillingCalculationService.AttrCalculatedKwh}' attribute.");
        }

        var now = timeProvider.GetUtcNow();

        // Levantamento always SETs the baseline (never accumulates) — see
        // IpBillingOrderTypes.PostingModeFor and this ticket's own "On Validate: SETS" note.
        var faturamento = await faturamentoRepository.PostOrderCalculationAsync(
            serviceOrderId,
            municipio.Id,
            competencia,
            calculatedKwh,
            FaturamentoPostingMode.Set,
            now,
            ct).ConfigureAwait(false);
        await faturamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        await levantamentoRepository.MarkValidatedAsync(serviceOrderId, ct).ConfigureAwait(false);
        await levantamentoRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        if (string.Equals(order.Status, IpBillingWorkflowGraph.Calculado, StringComparison.Ordinal))
        {
            await orderRepository.AppendActionAsync(
                serviceOrderId,
                new OrderActionLog(
                    OrderActionType.Validate,
                    performedBy ?? "system",
                    now.UtcDateTime,
                    $"Validated and posted (Set) to Faturamento {competencia}",
                    IpBillingWorkflowGraph.Validado),
                ct).ConfigureAwait(false);
        }

        return new LevantamentoValidationResult(serviceOrderId, IpBillingWorkflowGraph.Validado, faturamento);
    }

    private async Task<ServiceOrder> LoadMutableOrderAsync(string serviceOrderId, CancellationToken ct)
    {
        var loaded = await orderRepository.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{serviceOrderId}' not found.");

        return loaded as ServiceOrder
            ?? throw new InvalidOperationException($"Expected mutable {nameof(ServiceOrder)}.");
    }

    private static (string IbgeCode, string Competencia, int DaysInMonth, DateOnly CompetenciaStart, DateOnly DefaultAsOf)
        ReadOrderBillingMetadata(ServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(IpBillingCalculationService.AttrMunicipioIbgeCode, out var ibgeCode) ||
            string.IsNullOrWhiteSpace(ibgeCode))
        {
            throw new InvalidOperationException(
                $"Order '{order.Id}' is missing required attribute '{IpBillingCalculationService.AttrMunicipioIbgeCode}'.");
        }

        if (!order.Attributes.TryGetValue(IpBillingCalculationService.AttrCompetencia, out var competencia) ||
            !TryParseCompetenciaStart(competencia, out var competenciaStart))
        {
            throw new InvalidOperationException(
                $"Order '{order.Id}' is missing a valid 'yyyy-MM' attribute '{IpBillingCalculationService.AttrCompetencia}'.");
        }

        var daysInMonth = DateTime.DaysInMonth(competenciaStart.Year, competenciaStart.Month);
        var lastDay = new DateOnly(competenciaStart.Year, competenciaStart.Month, daysInMonth);
        return (ibgeCode, competencia, daysInMonth, competenciaStart, lastDay);
    }

    private static DateOnly ParseCompetenciaStart(string competencia) =>
        TryParseCompetenciaStart(competencia, out var start)
            ? start
            : throw new InvalidOperationException($"'{competencia}' is not a valid 'yyyy-MM' competência.");

    private static bool TryParseCompetenciaStart(string competencia, out DateOnly start) =>
        DateOnly.TryParseExact(
            $"{competencia}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);

    private static decimal ComputeDailyNormalKwhRate(
        IReadOnlyList<LevantamentoLampDetail> details, IReadOnlyList<LampType> catalog)
    {
        decimal dailyKwh = 0m;
        foreach (var detail in details)
        {
            if (detail.Quantity <= 0) continue;
            var billedWatts = ResolveTotalBilledWatts(catalog, detail.Code, detail.WattageW);
            dailyKwh += detail.Quantity * (billedWatts / 1000m) * IpEnergyMath.NormalLampDailyHours;
        }

        return dailyKwh;
    }

    private async Task<List<Levantamento24hLine>> ComputeIrregular24hLinesAsync(
        string serviceOrderId,
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

        var lines = new List<Levantamento24hLine>();
        foreach (var feature in page.Items)
        {
            var line = EvaluateLampGroup24hFeature(serviceOrderId, feature, catalog, daysInMonth, asOf);
            if (line is not null) lines.Add(line);
        }

        return lines;
    }

    private static Levantamento24hLine? EvaluateLampGroup24hFeature(
        string serviceOrderId,
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
            return null;
        }

        if (!attrs.TryGetValue("lampType", out var rawCode) ||
            !Enum.TryParse<LampTypeCode>(rawCode, ignoreCase: true, out var code) ||
            !attrs.TryGetValue("wattageW", out var rawWattage) ||
            !int.TryParse(rawWattage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wattageW))
        {
            return null;
        }

        var quantity = 1;
        if (attrs.TryGetValue("quantity", out var rawQty) &&
            int.TryParse(rawQty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQty))
        {
            quantity = parsedQty;
        }

        if (quantity <= 0 || !Guid.TryParse(feature.Id, out var featureId)) return null;

        var billedWatts = ResolveTotalBilledWatts(catalog, code, wattageW);
        var kwh = IpEnergyMath.Round(quantity * (billedWatts / 1000m) * IpEnergyMath.Irregular24hDailyHours * daysInMonth);

        return new Levantamento24hLine
        {
            ServiceOrderId = serviceOrderId,
            FeatureId = featureId,
            Code = code,
            WattageW = wattageW,
            Quantity = quantity,
            Kwh = kwh,
        };
    }

    private static decimal ResolveTotalBilledWatts(IReadOnlyList<LampType> catalog, LampTypeCode code, int wattageW)
    {
        var lampType = catalog.FirstOrDefault(t => t.Code == code);
        var option = lampType?.WattageOptions.FirstOrDefault(w => w.WattageW == wattageW);
        return option?.TotalBilledW ?? wattageW;
    }

    private static void StampCalculationAttributes(
        ServiceOrder order, decimal normalKwh, decimal retroativoKwh, decimal irregular24hKwh, decimal totalKwh)
    {
        order.Attributes[IpBillingCalculationService.AttrNormalKwh] = normalKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[AttrRetroativoKwh] = retroativoKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[IpBillingCalculationService.AttrIrregular24hKwh] = irregular24hKwh.ToString("F4", CultureInfo.InvariantCulture);
        order.Attributes[IpBillingCalculationService.AttrCalculatedKwh] = totalKwh.ToString("F4", CultureInfo.InvariantCulture);
    }
}
