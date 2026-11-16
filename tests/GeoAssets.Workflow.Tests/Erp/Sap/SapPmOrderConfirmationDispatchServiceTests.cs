using FluentAssertions;
using GeoAssets.Workflow.Erp.Sap;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sap;

/// <summary>
/// XD01-231's AC2: exactly one outbound SAP PM confirmation (BAPI) call per order, deduplicated
/// on retry, for <see cref="SapPmOrderConfirmationDispatchService"/>.
/// </summary>
public class SapPmOrderConfirmationDispatchServiceTests
{
    private sealed class InMemoryServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = new(StringComparer.Ordinal);

        public void Seed(ServiceOrder order) => _orders[order.Id] = order;

        public ServiceOrder? Find(string id) => _orders.GetValueOrDefault(id);

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(_orders.GetValueOrDefault(id));

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            if (!_orders.ContainsKey(order.Id))
                throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

            _orders[order.Id] = (ServiceOrder)order;
            return Task.CompletedTask;
        }

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
        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }

    private sealed class FakeSapPmConfirmationDispatcher(Exception? failure = null) : ISapPmConfirmationDispatcher
    {
        public int CallCount { get; private set; }
        public SapPmOrderConfirmation? LastConfirmation { get; private set; }

        public Task DispatchAsync(SapPmOrderConfirmation confirmation, CancellationToken ct = default)
        {
            CallCount++;
            LastConfirmation = confirmation;
            if (failure is not null)
                throw failure;
            return Task.CompletedTask;
        }
    }

    private static readonly OrderType FieldWorkOrderType = FieldWorkOrderTypes.Create();
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<SapPmMaterialLine> Materials = [new("MAT-1", 3m)];
    private const string EvidenceDossierUrl = "https://evidence.example/dossier-1";
    private const decimal LaborHours = 2.5m;

    private static ServiceOrder OrderAt(string id, string status, string? orderNumber = "4500001234") => new()
    {
        Id = id,
        Status = status,
        Attributes = orderNumber is null
            ? []
            : new Dictionary<string, string>(StringComparer.Ordinal) { [SapPmOrderAttributes.OrderNumberKey] = orderNumber },
    };

    private static SapPmOrderConfirmationDispatchService CreateService(
        InMemoryServiceOrderRepository repository, FakeSapPmConfirmationDispatcher dispatcher, TimeProvider? timeProvider = null) =>
        new(repository, dispatcher, timeProvider ?? new FakeTimeProvider(FixedNow));

    [Fact]
    public async Task TransitionToInspectedAsync_HappyPath_TransitionsAndDispatchesExactlyOnce()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OrderAt("order-1", FieldWorkStatus.Completed));
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeTrue();
        result.ConfirmationFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(1);
        dispatcher.LastConfirmation.Should().NotBeNull();
        dispatcher.LastConfirmation!.OrderNumber.Should().Be("4500001234");
        dispatcher.LastConfirmation.LaborHours.Should().Be(LaborHours);
        dispatcher.LastConfirmation.Materials.Should().BeEquivalentTo(Materials);
        dispatcher.LastConfirmation.EvidenceDossierUrl.Should().Be(EvidenceDossierUrl);
        dispatcher.LastConfirmation.ConfirmedAtUtc.Should().Be(FixedNow);

        var persisted = repository.Find("order-1")!;
        persisted.Status.Should().Be(FieldWorkStatus.Inspected);
        persisted.Attributes[SapPmOrderAttributes.EvidenceDossierUrlKey].Should().Be(EvidenceDossierUrl);
        persisted.Attributes[SapPmOrderAttributes.ConfirmationDispatchedAtKey]
            .Should().Be(FixedNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TransitionToInspectedAsync_AlreadyDispatched_SkipsDispatchWithoutAttemptingTransition()
    {
        var repository = new InMemoryServiceOrderRepository();
        var order = OrderAt("order-1", FieldWorkStatus.Inspected);
        order.Attributes[SapPmOrderAttributes.ConfirmationDispatchedAtKey] = "2025-12-31T00:00:00.0000000+00:00";
        repository.Seed(order);
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeFalse();
        result.ConfirmationFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(0);
        repository.Find("order-1")!.Status.Should().Be(FieldWorkStatus.Inspected);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_AlreadyDispatchedFlagSetBeforeTransitionWasAttempted_SkipsAndLeavesStatusUnchanged()
    {
        // Pure idempotency check: the "already dispatched" guard runs BEFORE the status
        // transition is attempted, so even an order that hasn't reached Inspected yet is left
        // completely untouched once the dispatch flag is set.
        var repository = new InMemoryServiceOrderRepository();
        var order = OrderAt("order-1", FieldWorkStatus.Completed);
        order.Attributes[SapPmOrderAttributes.ConfirmationDispatchedAtKey] = "2025-12-31T00:00:00.0000000+00:00";
        repository.Seed(order);
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeFalse();
        result.ConfirmationFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(0);
        repository.Find("order-1")!.Status.Should().Be(FieldWorkStatus.Completed);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_DispatcherThrowsInvalidOperationException_ReturnsFailureReasonButKeepsTransition()
    {
        const string message = "boom - invalid state on SAP side";
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OrderAt("order-1", FieldWorkStatus.Completed));
        var dispatcher = new FakeSapPmConfirmationDispatcher(new InvalidOperationException(message));
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeFalse();
        result.ConfirmationFailureReason.Should().Be(message);
        dispatcher.CallCount.Should().Be(1);

        var persisted = repository.Find("order-1")!;
        persisted.Status.Should().Be(FieldWorkStatus.Inspected);
        persisted.Attributes.Should().NotContainKey(SapPmOrderAttributes.ConfirmationDispatchedAtKey);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_DispatcherThrowsHttpRequestException_ReturnsFailureReasonButKeepsTransition()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OrderAt("order-1", FieldWorkStatus.Completed));
        var dispatcher = new FakeSapPmConfirmationDispatcher(new HttpRequestException("network unreachable"));
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeFalse();
        result.ConfirmationFailureReason.Should().Be("network unreachable");
        dispatcher.CallCount.Should().Be(1);

        var persisted = repository.Find("order-1")!;
        persisted.Status.Should().Be(FieldWorkStatus.Inspected);
        persisted.Attributes.Should().NotContainKey(SapPmOrderAttributes.ConfirmationDispatchedAtKey);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_NoRecordedOrderNumber_ReturnsFailureWithoutCallingDispatcher()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OrderAt("order-1", FieldWorkStatus.Completed, orderNumber: null));
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        result.ConfirmationDispatched.Should().BeFalse();
        result.ConfirmationFailureReason.Should().NotBeNullOrEmpty();
        result.ConfirmationFailureReason.Should().Contain("order-1");
        dispatcher.CallCount.Should().Be(0);
        repository.Find("order-1")!.Status.Should().Be(FieldWorkStatus.Inspected);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_UnknownOrderId_ThrowsKeyNotFoundException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = async () => await service.TransitionToInspectedAsync("missing-order", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TransitionToInspectedAsync_StructurallyIllegalTransition_ThrowsAndDoesNotDispatch()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OrderAt("order-1", FieldWorkStatus.Draft));
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = async () => await service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        dispatcher.CallCount.Should().Be(0);
        repository.Find("order-1")!.Status.Should().Be(FieldWorkStatus.Draft);
    }

    [Fact]
    public Task TransitionToInspectedAsync_EmptyOrderId_ThrowsArgumentException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionToInspectedAsync(string.Empty, LaborHours, Materials, EvidenceDossierUrl, FieldWorkOrderType);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public Task TransitionToInspectedAsync_NullMaterials_ThrowsArgumentNullException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionToInspectedAsync("order-1", LaborHours, null!, EvidenceDossierUrl, FieldWorkOrderType);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public Task TransitionToInspectedAsync_EmptyEvidenceDossierUrl_ThrowsArgumentException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionToInspectedAsync("order-1", LaborHours, Materials, string.Empty, FieldWorkOrderType);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public Task TransitionToInspectedAsync_NullOrderType_ThrowsArgumentNullException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeSapPmConfirmationDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionToInspectedAsync("order-1", LaborHours, Materials, EvidenceDossierUrl, null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
