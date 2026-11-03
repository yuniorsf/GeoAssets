using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

public class FiscalizacaoCalculationServiceTests
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

        // Explicit no-op accessors (not field-like events) — this fake doesn't support
        // subscribing to/observing these events by design; nothing in these tests needs to.
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

    private sealed class FakeFiscalizacaoRepository : IFiscalizacaoRepository
    {
        private readonly Dictionary<string, FiscalizacaoHeader> _headers = [];
        private readonly Dictionary<string, List<FiscalizacaoLine>> _lines = [];

        public Task<FiscalizacaoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task AddHeaderAsync(FiscalizacaoHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FiscalizacaoLine>> GetLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FiscalizacaoLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceLinesAsync(string serviceOrderId, IReadOnlyList<FiscalizacaoLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ServiceOrder NewFiscalizacaoOrder(string id, string competencia) => new()
    {
        Id = id,
        OrderTypeId = "ip-fiscalizacao",
        Status = "Carregado",
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["municipioIbgeCode"] = IbgeCode,
            ["competencia"] = competencia,
        },
    };

    private static (FiscalizacaoCalculationService Service, FakeServiceOrderRepository OrderRepo, FakeFiscalizacaoRepository FiscalizacaoRepo) NewService()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var fiscalizacaoRepo = new FakeFiscalizacaoRepository();
        var service = new FiscalizacaoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), fiscalizacaoRepo, TimeProvider.System);
        return (service, orderRepo, fiscalizacaoRepo);
    }

    [Fact]
    public async Task UploadAsync_PersistsLinesWithoutComputingTotals()
    {
        var (service, orderRepo, fiscalizacaoRepo) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var count = await service.UploadAsync("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
        ]);

        count.Should().Be(1);
        var lines = await fiscalizacaoRepo.GetLinesAsync("order-1");
        lines.Should().ContainSingle();
        lines[0].Agregado.Should().Be(0, "UploadAsync must not compute totals — CalculateAsync does");
    }

    [Fact]
    public async Task CalculateAsync_AllFourGroupsPresent_ComputesTotalsAndTransitionsToCalculado()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var rows = new[]
        {
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Novos24hIrregulares, 4),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Regularizacao24h, 3),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.Status.Should().Be("Calculado");
        result.TotalAgregado.Should().Be(3);
        result.TotalRetroativo.Should().Be(2);
        result.Lines.Should().ContainSingle();
        order.Status.Should().Be("Calculado");
    }

    [Fact]
    public async Task CalculateAsync_NegativeAgregadoAcrossMultipleLines_SumsCorrectly_NotClamped()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var rows = new[]
        {
            // INST-001: Agregado = 2 - 20 = -18.
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 2),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 20),
            // INST-002: Agregado = 5.
            new FiscalizacaoWorkbookRow("INST-002", FiscalizacaoOperationGroup.Fiscalizacao, 5),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.TotalAgregado.Should().Be(-13);
    }

    [Fact]
    public async Task CalculateStoredAsync_RederivesRowsFromPersistedLines()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);
        await service.UploadAsync("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 4),
        ]);

        var result = await service.CalculateStoredAsync("order-1");

        result.TotalAgregado.Should().Be(6);
        result.TotalRetroativo.Should().Be(6);
    }

    [Fact]
    public async Task ValidateAsync_MarksHeaderValidated_AndTransitionsOrder_WithoutPostingToFaturamento()
    {
        var (service, orderRepo, fiscalizacaoRepo) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);
        await service.CalculateAsync("order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10)]);

        var result = await service.ValidateAsync("order-1");

        result.Status.Should().Be("Validado");
        result.TotalAgregado.Should().Be(10);
        order.Status.Should().Be("Validado");
        (await fiscalizacaoRepo.GetHeaderAsync("order-1"))!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public Task CalculateAsync_WrongStatus_Throws()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        order.Status = "Validado";
        orderRepo.Seed(order);

        var act = () => service.CalculateAsync("order-1", []);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }
}
