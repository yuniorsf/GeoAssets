using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Oficio;

public class OficioCalculationServiceTests
{
    private const string IbgeCode = "3550308";
    private static readonly Guid MunicipioId = Guid.NewGuid();

    private sealed class FakeServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = [];
        public readonly List<OrderActionLog> AppendedActions = [];

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
            AppendedActions.Add(entry);
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

    private sealed class FakeOficioRepository : IOficioRepository
    {
        private readonly Dictionary<string, OficioHeader> _headers = [];
        private readonly Dictionary<string, List<OficioLine>> _lines = [];

        public Task<OficioHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task<OficioHeader?> GetHeaderByNumeroAsync(Guid municipioId, string numeroPrefeitura, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values.FirstOrDefault(h =>
                h.MunicipioId == municipioId && string.Equals(h.NumeroPrefeitura, numeroPrefeitura, StringComparison.Ordinal)));

        public Task AddHeaderAsync(OficioHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task ClearNumeroPrefeituraAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].NumeroPrefeitura = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OficioLine>> GetLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OficioLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceLinesAsync(string serviceOrderId, IReadOnlyList<OficioLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ServiceOrder NewOficioOrder(string id, string competencia) => new()
    {
        Id = id,
        OrderTypeId = "ip-oficio",
        Status = "Carregado",
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["municipioIbgeCode"] = IbgeCode,
            ["competencia"] = competencia,
        },
    };

    private static (OficioCalculationService Service, FakeServiceOrderRepository OrderRepo, FakeOficioRepository OficioRepo) NewService()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var oficioRepo = new FakeOficioRepository();
        var service = new OficioCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), oficioRepo, TimeProvider.System);
        return (service, orderRepo, oficioRepo);
    }

    [Fact]
    public async Task UploadAsync_PersistsLinesWithoutComputingTotals()
    {
        var (service, orderRepo, oficioRepo) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));

        var count = await service.UploadAsync("order-1", "OF-001",
        [
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10),
        ]);

        count.Should().Be(1);
        var lines = await oficioRepo.GetLinesAsync("order-1");
        lines.Should().ContainSingle();
        lines[0].Agregado.Should().Be(0, "UploadAsync must not compute totals — CalculateAsync does");
    }

    [Fact]
    public async Task UploadAsync_NullNumero_NeverTriggersDuplicateGuard()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        orderRepo.Seed(NewOficioOrder("order-2", "2027-01"));

        await service.UploadAsync("order-1", null, [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);
        var act = async () => await service.UploadAsync("order-2", null, [new OficioWorkbookRow("INST-002", OficioOperationGroup.Inclusao, 1)]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadAsync_DuplicateNumeroInSameMunicipio_DifferentOrder_Throws()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        orderRepo.Seed(NewOficioOrder("order-2", "2027-01"));
        await service.UploadAsync("order-1", "OF-001", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);

        var act = async () => await service.UploadAsync("order-2", "OF-001", [new OficioWorkbookRow("INST-002", OficioOperationGroup.Inclusao, 1)]);

        var ex = await act.Should().ThrowAsync<DuplicateOficioNumeroException>();
        ex.Which.NumeroPrefeitura.Should().Be("OF-001");
        ex.Which.ExistingServiceOrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task UploadAsync_SameOrderReuploadingSameNumero_DoesNotThrow()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        await service.UploadAsync("order-1", "OF-001", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);

        var act = async () => await service.UploadAsync("order-1", "OF-001", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 2)]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SupersedeAsync_ReleasesNumero_AllowingAnotherOrderToClaimIt()
    {
        var (service, orderRepo, oficioRepo) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        orderRepo.Seed(NewOficioOrder("order-2", "2027-01"));
        await service.UploadAsync("order-1", "OF-001", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);

        await service.SupersedeAsync("order-1");
        await service.UploadAsync("order-2", "OF-001", [new OficioWorkbookRow("INST-002", OficioOperationGroup.Inclusao, 1)]);

        (await oficioRepo.GetHeaderAsync("order-1"))!.NumeroPrefeitura.Should().BeNull();
        (await oficioRepo.GetHeaderAsync("order-2"))!.NumeroPrefeitura.Should().Be("OF-001");
    }

    [Fact]
    public async Task SupersedeAsync_LogsAnExplicitActionAgainstTheExistingOrder()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        await service.UploadAsync("order-1", "OF-001", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);

        await service.SupersedeAsync("order-1", performedBy: "alice");

        orderRepo.AppendedActions.Should().ContainSingle(a =>
            a.PerformedBy == "alice" && a.Comment != null && a.Comment.Contains("OF-001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SupersedeAsync_NoNumeroToSupersede_Throws()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        await service.UploadAsync("order-1", null, [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1)]);

        var act = async () => await service.SupersedeAsync("order-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CalculateAsync_AllFourGroupsPresent_ComputesTotalsAndTransitionsToCalculado()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewOficioOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var rows = new[]
        {
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Retirada, 8),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Novos24hIrregulares, 4),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Regularizacao24h, 3),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.Status.Should().Be("Calculado");
        result.TotalAgregado.Should().Be(3);
        result.Lines.Should().ContainSingle();
        result.Swaps.Should().ContainSingle(s => s.IsSwap, "this line carries both inclusão and retirada");
        order.Status.Should().Be("Calculado");
    }

    [Fact]
    public async Task CalculateAsync_LineWithInclusaoAndRetirada_SwapFlagged()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));

        var rows = new[]
        {
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 1),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Retirada, 1),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.Swaps.Should().ContainSingle(s => s.IsSwap);
    }

    [Fact]
    public async Task CalculateAsync_NegativeAgregadoAcrossMultipleLines_SumsCorrectly_NotClamped()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));

        var rows = new[]
        {
            // INST-001: Agregado = 2 - 20 = -18.
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 2),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Retirada, 20),
            // INST-002: Agregado = 5.
            new OficioWorkbookRow("INST-002", OficioOperationGroup.Inclusao, 5),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.TotalAgregado.Should().Be(-13);
    }

    [Fact]
    public async Task CalculateStoredAsync_RederivesRowsFromPersistedLines()
    {
        var (service, orderRepo, _) = NewService();
        orderRepo.Seed(NewOficioOrder("order-1", "2027-01"));
        await service.UploadAsync("order-1", "OF-001",
        [
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Retirada, 4),
        ]);

        var result = await service.CalculateStoredAsync("order-1");

        result.TotalAgregado.Should().Be(6);
    }

    [Fact]
    public async Task ValidateAsync_MarksHeaderValidated_AndTransitionsOrder_WithoutPostingToFaturamento()
    {
        var (service, orderRepo, oficioRepo) = NewService();
        var order = NewOficioOrder("order-1", "2027-01");
        orderRepo.Seed(order);
        await service.CalculateAsync("order-1", [new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10)]);

        var result = await service.ValidateAsync("order-1");

        result.Status.Should().Be("Validado");
        result.TotalAgregado.Should().Be(10);
        order.Status.Should().Be("Validado");
        (await oficioRepo.GetHeaderAsync("order-1"))!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public Task CalculateAsync_WrongStatus_Throws()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewOficioOrder("order-1", "2027-01");
        order.Status = "Validado";
        orderRepo.Seed(order);

        var act = () => service.CalculateAsync("order-1", []);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }
}
