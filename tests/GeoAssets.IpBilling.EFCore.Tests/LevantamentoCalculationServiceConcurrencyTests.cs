using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// Proves the AC item a lighter in-memory <c>IServiceOrderRepository</c> fake can't:
/// <c>LevantamentoCalculationService</c>'s <c>CalculateAsync</c>/<c>ValidateAsync</c> calls
/// surface <c>ServiceOrderConcurrencyException</c> when two writers race on the same
/// <c>ServiceOrder.RowVersion</c>, using a real <c>EFServiceOrderRepository</c> over SQLite —
/// <c>IServiceOrderWriter.UpdateAsync</c>'s own doc comment flags this as "only
/// EFServiceOrderRepository currently detects this" (XD01-26), so this is the only repository
/// implementation that can actually exercise it. The non-ServiceOrder dependencies
/// (<see cref="ILevantamentoRepository"/>, etc.) don't need real persistence for this —
/// concurrency is specifically a <c>ServiceOrder.RowVersion</c> property, so minimal in-memory
/// fakes are used for everything else.
/// </summary>
public class LevantamentoCalculationServiceConcurrencyTests
{
    private const string IbgeCode = "3550308";

    private sealed class FakeMunicipioRepository(Municipio municipio) : IMunicipioRepository
    {
        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Municipio?>(municipio);
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) => Task.FromResult<Municipio?>(municipio);
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Municipio>>([municipio]);
        public Task AddAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLampTypeRepository : ILampTypeRepository
    {
        private readonly List<LampType> _catalog =
            [new LampType { Code = LampTypeCode.LD, Name = "LED", WattageOptions = [new LampWattageOption { WattageW = 100, BallastLossW = 0 }] }];

        public Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default) => Task.FromResult(_catalog.FirstOrDefault(l => l.Code == code));
        public Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LampType>>(_catalog);
    }

    private sealed class FakeLevantamentoRepository : ILevantamentoRepository
    {
        private readonly Dictionary<string, LevantamentoHeader> _headers = [];
        private readonly Dictionary<string, List<LevantamentoTransformerLine>> _lines = [];

        public Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) => Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));
        public Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default) { _headers[header.ServiceOrderId] = header; return Task.CompletedTask; }
        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default) { _headers[serviceOrderId].IsValidated = true; return Task.CompletedTask; }
        public Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LevantamentoTransformerLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);
        public Task ReplaceTransformerLinesAsync(string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default) { _lines[serviceOrderId] = [.. lines]; return Task.CompletedTask; }
        public Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(string serviceOrderId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Levantamento24hLine>>([]);
        public Task Replace24hLinesAsync(string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default) => Task.CompletedTask;
        public Task<LevantamentoHeader?> GetMostRecentValidatedAsync(Guid municipioId, string beforeCompetencia, CancellationToken ct = default) => Task.FromResult<LevantamentoHeader?>(null);
        public Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default) => Task.FromResult<LevantamentoHeader?>(null);
        public Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(Guid municipioId, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTransformerResolver : ITransformerResolver
    {
        public Task<Guid?> ResolveTransformerFeatureIdAsync(string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);
    }

    private sealed class FakeFaturamentoRepository : IFaturamentoRepository
    {
        public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) => Task.FromResult<Faturamento?>(null);
        public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Faturamento>>([]);
        public Task AddAsync(Faturamento faturamento, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Faturamento> PostOrderCalculationAsync(string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline, FaturamentoPostingMode mode, DateTimeOffset postedAt, CancellationToken ct = default) =>
            Task.FromResult(new Faturamento { MunicipioId = municipioId, Competencia = competencia, KwhFaturado = kwhDeltaOrBaseline, CreatedAt = postedAt });
        public Task<Faturamento> OverwriteManualEntryAsync(Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference, DateTimeOffset postedAt, CancellationToken ct = default) =>
            Task.FromResult(new Faturamento { MunicipioId = municipioId, Competencia = competencia, KwhFaturado = kwhFaturado, CreatedAt = postedAt });
        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FaturamentoPosting>>([]);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class EmptyGeoCoreClient : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) => Task.FromResult(new PagedResult<GeoFeature> { Items = [], TotalCount = 0 });
        public Task<Core.Messaging.Events.AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(Core.Messaging.Events.AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static OrderTypeRegistry BuildOrderTypeRegistry()
    {
        var registry = new OrderTypeRegistry();
        IpBillingOrderTypes.RegisterAll(registry);
        return registry;
    }

    private static LevantamentoCalculationService BuildService(ServiceOrderDbContext db, Municipio municipio) =>
        new(
            new EFServiceOrderRepository(db, orderTypeRegistry: BuildOrderTypeRegistry()),
            new FakeMunicipioRepository(municipio),
            new FakeLampTypeRepository(),
            new FakeLevantamentoRepository(),
            new FakeTransformerResolver(),
            new NullInterveningOperationLookup(),
            new FakeFaturamentoRepository(),
            new EmptyGeoCoreClient(),
            TimeProvider.System);

    [Fact]
    public async Task CalculateAsync_TwoConcurrentWriters_SecondThrowsServiceOrderConcurrencyException()
    {
        using var fixture = new ServiceOrderSqliteFixture();
        var municipio = new Municipio { Id = Guid.NewGuid(), IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };

        var seedRepo = new EFServiceOrderRepository(fixture.Context);
        var order = new ServiceOrder
        {
            Id = "order-race",
            OrderTypeId = "ip-levantamento",
            Status = "Carregado",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["municipioIbgeCode"] = IbgeCode,
                ["competencia"] = "2027-01",
            },
        };
        await seedRepo.AddAsync(order);

        // Writer B reads first (its context now tracks RowVersion V1) — before Writer A ever runs.
        using var contextB = fixture.NewContext();
        var serviceB = BuildService(contextB, municipio);
        // Prime context B's tracker by reading the order through its own repository first,
        // the same "both read before either writes" shape as
        // EFServiceOrderRepositoryTests.UpdateAsync_ConcurrentWriters_ThrowsServiceOrderConcurrencyException.
        await new EFServiceOrderRepository(contextB).GetByIdAsync("order-race");

        // Writer A runs the full Calculate flow and commits first — bumps RowVersion to V2.
        var rows = new[] { new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10) };
        var serviceA = BuildService(fixture.Context, municipio);
        await serviceA.CalculateAsync("order-race", rows, new DateOnly(2027, 1, 31));

        // Writer B's context still believes RowVersion is V1 (its tracker was primed before A's
        // commit and EF Core's identity resolution won't silently refresh it) — its own
        // Calculate call re-reads via that same stale-tracked context and then tries to save
        // against a RowVersion the database no longer has.
        var act = () => serviceB.CalculateAsync("order-race", rows, new DateOnly(2027, 1, 31));

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()
            .Where(e => e.OrderId == "order-race");
    }
}
