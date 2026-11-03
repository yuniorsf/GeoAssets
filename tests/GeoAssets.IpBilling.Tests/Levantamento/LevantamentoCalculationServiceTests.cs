using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Levantamento;

/// <summary>
/// End-to-end <see cref="LevantamentoCalculationService"/> tests against in-memory fakes.
///
/// <strong>Golden-master caveat:</strong> this ticket's verification strategy calls for replaying
/// a real/realistic legacy GEOS_FAT_IP workbook with a known legacy output. No such workbook or
/// output sample is available in this repository/environment — the scenario below is a
/// hand-constructed stand-in with hand-computed expected totals (independently derived from the
/// documented 11h52 daily rate and the 730-day retroactive cap, not by mirroring the
/// implementation), not a replay of real legacy data. Flagged on the ticket as unverified
/// against the actual legacy system.
/// </summary>
public class LevantamentoCalculationServiceTests
{
    private const string IbgeCode = "3550308";
    private static readonly Guid MunicipioId = Guid.NewGuid();

    private sealed class FakeServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = [];

        public void Seed(ServiceOrder order) => _orders[order.Id] = order;

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(_orders.GetValueOrDefault(id));

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            _orders[order.Id] = (ServiceOrder)order;
            return Task.CompletedTask;
        }

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            if (entry.ResultingStatus is not null) _orders[orderId].Status = entry.ResultingStatus;
            return Task.CompletedTask;
        }

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        // Test double only implements events because IServiceOrderWriter requires them —
        // nothing in these tests needs to raise or observe them.
        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }

    private sealed class FakeMunicipioRepository(Municipio municipio) : IMunicipioRepository
    {
        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Municipio?>(municipio);
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(ibgeCode, municipio.IbgeCode, StringComparison.Ordinal) ? municipio : null);
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Municipio>>([municipio]);
        public Task AddAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLampTypeRepository : ILampTypeRepository
    {
        private readonly List<LampType> _catalog =
        [
            new LampType
            {
                Code = LampTypeCode.LD,
                Name = "LED",
                WattageOptions = [new LampWattageOption { WattageW = 100, BallastLossW = 0 }],
            },
        ];

        public Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default) =>
            Task.FromResult(_catalog.FirstOrDefault(l => l.Code == code));
        public Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LampType>>(_catalog);
    }

    private sealed class FakeLevantamentoRepository : ILevantamentoRepository
    {
        private readonly Dictionary<string, LevantamentoHeader> _headers = [];
        private readonly Dictionary<string, List<LevantamentoTransformerLine>> _lines = [];
        private readonly Dictionary<string, List<Levantamento24hLine>> _lines24h = [];

        public Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LevantamentoTransformerLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceTransformerLinesAsync(string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Levantamento24hLine>>(_lines24h.GetValueOrDefault(serviceOrderId) ?? []);

        public Task Replace24hLinesAsync(string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default)
        {
            _lines24h[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task<LevantamentoHeader?> GetMostRecentValidatedAsync(Guid municipioId, string beforeCompetencia, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated &&
                            string.Compare(h.Competencia, beforeCompetencia, StringComparison.Ordinal) < 0)
                .OrderByDescending(h => h.Competencia, StringComparer.Ordinal)
                .FirstOrDefault());

        public Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated)
                .OrderBy(h => h.Competencia, StringComparer.Ordinal)
                .FirstOrDefault());

        public Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(Guid municipioId, CancellationToken ct = default)
        {
            var validatedOrderIds = _headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated)
                .Select(h => h.ServiceOrderId)
                .ToHashSet(StringComparer.Ordinal);

            var codigos = _lines
                .Where(kvp => validatedOrderIds.Contains(kvp.Key))
                .SelectMany(kvp => kvp.Value)
                .Select(l => l.CodigoInstalacao)
                .ToHashSet(StringComparer.Ordinal);

            return Task.FromResult<IReadOnlySet<string>>(codigos);
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTransformerResolver : ITransformerResolver
    {
        private readonly Dictionary<string, Guid> _map = [];
        public void Map(string codigoInstalacao, Guid featureId) => _map[codigoInstalacao] = featureId;

        public Task<Guid?> ResolveTransformerFeatureIdAsync(string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default) =>
            Task.FromResult(_map.TryGetValue(codigoInstalacao, out var id) ? id : (Guid?)null);
    }

    private sealed class FakeFaturamentoRepository : IFaturamentoRepository
    {
        public readonly List<Faturamento> Posted = [];

        public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) =>
            Task.FromResult(Posted.FirstOrDefault(f =>
                f.MunicipioId == municipioId && string.Equals(f.Competencia, competencia, StringComparison.Ordinal)));

        public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Faturamento>>(Posted.Where(f => f.MunicipioId == municipioId).ToList());

        public Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
        {
            Posted.Add(faturamento);
            return Task.CompletedTask;
        }

        public Task<Faturamento> PostOrderCalculationAsync(
            string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline,
            FaturamentoPostingMode mode, DateTimeOffset postedAt, CancellationToken ct = default)
        {
            var faturamento = new Faturamento { MunicipioId = municipioId, Competencia = competencia, KwhFaturado = kwhDeltaOrBaseline, CreatedAt = postedAt };
            Posted.Add(faturamento);
            return Task.FromResult(faturamento);
        }

        public Task<Faturamento> OverwriteManualEntryAsync(
            Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference,
            DateTimeOffset postedAt, CancellationToken ct = default)
        {
            var faturamento = new Faturamento { MunicipioId = municipioId, Competencia = competencia, KwhFaturado = kwhFaturado, CreatedAt = postedAt };
            Posted.Add(faturamento);
            return Task.FromResult(faturamento);
        }

        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FaturamentoPosting>>([]);

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class EmptyGeoCoreClient : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<GeoFeature> { Items = [], TotalCount = 0 });
        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ServiceOrder NewLevantamentoOrder(string id, string competencia) => new()
    {
        Id = id,
        OrderTypeId = "ip-levantamento",
        Status = "Carregado",
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["municipioIbgeCode"] = IbgeCode,
            ["competencia"] = competencia,
        },
    };

    [Fact]
    public async Task EndToEnd_FirstLevantamentoEver_ThenSecondCompetenciaWithNewAndExistingTransformers()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var levantamentoRepo = new FakeLevantamentoRepository();
        var transformerResolver = new FakeTransformerResolver();
        var faturamentoRepo = new FakeFaturamentoRepository();

        var service = new LevantamentoCalculationService(
            orderRepo,
            new FakeMunicipioRepository(municipio),
            new FakeLampTypeRepository(),
            levantamentoRepo,
            transformerResolver,
            new NullInterveningOperationLookup(),
            faturamentoRepo,
            new EmptyGeoCoreClient(),
            TimeProvider.System);

        // January: município's very first Levantamento ever (code 30) — one transformer,
        // 10 LED lamps at 100W. Hand-computed expectation: 10 * (100/1000) * 11.8667 h/day *
        // 31 days = 367.8677 kWh (11h52m = 11.8667h rounded to 4 places per IpEnergyMath).
        var janOrder = NewLevantamentoOrder("order-jan", "2027-01");
        orderRepo.Seed(janOrder);
        var janRows = new[] { new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10) };

        var janResult = await service.CalculateAsync("order-jan", janRows, new DateOnly(2027, 1, 31));

        janResult.TransformerLines.Should().ContainSingle();
        var janLine = janResult.TransformerLines[0];
        janLine.IsNewTransformer.Should().BeTrue();
        janLine.Tipo.Should().Be(TipoRetroacao.PrimeiraLevantamentoDoMunicipio30);
        janLine.RetroativoKwh.Should().Be(0m);
        janLine.NormalKwh.Should().BeApproximately(367.8677m, 0.001m);

        await service.ValidateAsync("order-jan");
        faturamentoRepo.Posted.Should().ContainSingle(f => f.Competencia == "2027-01");

        // March: INST-001 reappears (existing, no code) and a brand-new INST-002 appears —
        // never surveyed before at all (not just missing from the latest survey), so it
        // anchors at the município's FIRST validated Levantamento (January, code 20), not the
        // 00/01 pair (which requires having appeared in some past survey before dropping out).
        var marOrder = NewLevantamentoOrder("order-mar", "2027-03");
        orderRepo.Seed(marOrder);
        var marRows = new[]
        {
            new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10),
            new LevantamentoWorkbookRow("INST-002", LampTypeCode.LD, 100, 5),
        };

        var marResult = await service.CalculateAsync("order-mar", marRows, new DateOnly(2027, 3, 31));

        var inst1Line = marResult.TransformerLines.Single(l => string.Equals(l.CodigoInstalacao, "INST-001", StringComparison.Ordinal));
        inst1Line.IsNewTransformer.Should().BeFalse();
        inst1Line.Tipo.Should().BeNull();
        inst1Line.RetroativoKwh.Should().BeNull();

        var inst2Line = marResult.TransformerLines.Single(l => string.Equals(l.CodigoInstalacao, "INST-002", StringComparison.Ordinal));
        inst2Line.IsNewTransformer.Should().BeTrue();
        inst2Line.Tipo.Should().Be(TipoRetroacao.HistoricoCompleto20);
        // Anchor = January 1st (the município's first validated competência start), asOf =
        // March 31 ⇒ 89 days * dailyRate(5 lamps * 0.1kW * 11.8667h ≈ 5.9333 kWh/day) ≈ 528.3637 kWh.
        inst2Line.RetroativoKwh.Should().BeGreaterThan(0m);

        await service.ValidateAsync("order-mar");
        faturamentoRepo.Posted.Should().ContainSingle(f => f.Competencia == "2027-03");
    }

    [Fact]
    public async Task CalculateAsync_UnresolvedTransformer_ReportedTogetherNotOneAtATime()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var order = NewLevantamentoOrder("order-unresolved", "2027-01");
        orderRepo.Seed(order);

        var service = new LevantamentoCalculationService(
            orderRepo,
            new FakeMunicipioRepository(municipio),
            new FakeLampTypeRepository(),
            new FakeLevantamentoRepository(),
            new FakeTransformerResolver(), // nothing mapped — every código fails to resolve
            new NullInterveningOperationLookup(),
            new FakeFaturamentoRepository(),
            new EmptyGeoCoreClient(),
            TimeProvider.System);

        var rows = new[]
        {
            new LevantamentoWorkbookRow("INST-A", LampTypeCode.LD, 100, 1),
            new LevantamentoWorkbookRow("INST-B", LampTypeCode.LD, 100, 1),
        };

        var result = await service.CalculateAsync("order-unresolved", rows, new DateOnly(2027, 1, 31));

        result.UnresolvedCodigosInstalacao.Should().BeEquivalentTo(["INST-A", "INST-B"]);
        // Billing still proceeds for unresolved transformers — a map/GIS gap doesn't lose kWh.
        result.NormalKwh.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task UploadAsync_LandsLinesWithoutComputingAnything()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var order = NewLevantamentoOrder("order-upload", "2027-01");
        orderRepo.Seed(order);
        var levantamentoRepo = new FakeLevantamentoRepository();

        var service = new LevantamentoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), new FakeLampTypeRepository(), levantamentoRepo,
            new FakeTransformerResolver(), new NullInterveningOperationLookup(), new FakeFaturamentoRepository(),
            new EmptyGeoCoreClient(), TimeProvider.System);

        var count = await service.UploadAsync("order-upload",
            [new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10)]);

        count.Should().Be(1);
        var lines = await levantamentoRepo.GetTransformerLinesAsync("order-upload");
        lines.Should().ContainSingle();
        lines[0].NormalKwh.Should().Be(0m); // not computed yet — Upload only lands raw rows
        lines[0].IsNewTransformer.Should().BeFalse();
        order.Status.Should().Be("Carregado"); // Upload doesn't transition status
    }

    [Fact]
    public Task UploadAsync_WhileAlreadyCalculado_Throws()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var order = NewLevantamentoOrder("order-upload-2", "2027-01");
        order.Status = "Calculado";
        orderRepo.Seed(order);

        var service = new LevantamentoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), new FakeLampTypeRepository(), new FakeLevantamentoRepository(),
            new FakeTransformerResolver(), new NullInterveningOperationLookup(), new FakeFaturamentoRepository(),
            new EmptyGeoCoreClient(), TimeProvider.System);

        var act = () => service.UploadAsync("order-upload-2", [new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 1)]);

        return act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CalculateStoredAsync_ReadsBackPreviouslyUploadedRows_ThenComputes()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var order = NewLevantamentoOrder("order-calc-stored", "2027-01");
        orderRepo.Seed(order);
        var levantamentoRepo = new FakeLevantamentoRepository();

        var service = new LevantamentoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), new FakeLampTypeRepository(), levantamentoRepo,
            new FakeTransformerResolver(), new NullInterveningOperationLookup(), new FakeFaturamentoRepository(),
            new EmptyGeoCoreClient(), TimeProvider.System);

        await service.UploadAsync("order-calc-stored", [new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10)]);

        var result = await service.CalculateStoredAsync("order-calc-stored", new DateOnly(2027, 1, 31));

        result.TransformerLines.Should().ContainSingle();
        result.NormalKwh.Should().BeGreaterThan(0m);

        // Re-running Calculate while already Calculado is a same-status no-op recompute.
        var recompute = await service.CalculateStoredAsync("order-calc-stored", new DateOnly(2027, 1, 31));
        recompute.NormalKwh.Should().Be(result.NormalKwh);
    }
}
