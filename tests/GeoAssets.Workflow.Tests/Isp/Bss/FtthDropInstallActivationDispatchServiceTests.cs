using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.Bss;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.Bss;

public class FtthDropInstallActivationDispatchServiceTests
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

    private sealed class FakeBssOssOutboundWebhookDispatcher : IBssOssOutboundWebhookDispatcher
    {
        private readonly Exception? _failure;

        public FakeBssOssOutboundWebhookDispatcher(Exception? failure = null) => _failure = failure;

        public string? LastProviderId { get; private set; }
        public string? LastPayloadJson { get; private set; }
        public int CallCount { get; private set; }

        public Task DispatchAsync(string providerId, string payloadJson, CancellationToken ct = default)
        {
            CallCount++;
            LastProviderId = providerId;
            LastPayloadJson = payloadJson;
            if (_failure is not null)
                throw _failure;
            return Task.CompletedTask;
        }
    }

    private static readonly OrderType FtthOrderType =
        FtthDropInstallWorkflowGraph.BuildOrderType(FtthDropInstallOrderTypes.FtthDropInstallId, "FTTH Drop Install");

    private static ServiceOrder NewOrder(string id, string status) => new() { Id = id, Status = status };

    private static ServiceOrder ActivatableOrder(string id, string providerId = "IXC") => new()
    {
        Id = id,
        Status = FtthDropInstallStatus.OntProvisioned,
        Attributes =
        {
            [BssOrderAttributes.ProviderIdKey] = providerId,
            [BssOrderAttributes.SubscriberIdKey] = "cli-1",
            [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
            [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
        },
    };

    private static FtthDropInstallActivationDispatchService CreateService(
        InMemoryServiceOrderRepository repository, FakeBssOssOutboundWebhookDispatcher dispatcher) =>
        new(repository, new BssOssOrderAdapterRegistry([new IxcSoftOrderAdapter()]), dispatcher, new FakeTimeProvider());

    [Fact]
    public async Task TransitionAsync_NonActivatedTransition_PersistsStatusAndDoesNotDispatch()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(NewOrder("order-1", FtthDropInstallStatus.CrmSale));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.CtoPortTest, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeFalse();
        result.ActivationWebhookFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(0);
        repository.Find("order-1")!.Status.Should().Be(FtthDropInstallStatus.CtoPortTest);
    }

    [Fact]
    public async Task TransitionAsync_ToActivatedWithValidIxcOrder_DispatchesAdapterBuiltPayload()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(ActivatableOrder("order-1"));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeTrue();
        result.ActivationWebhookFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(1);
        dispatcher.LastProviderId.Should().Be("IXC");
        dispatcher.LastPayloadJson.Should().Contain("cli-1").And.Contain("FSAN123");
    }

    [Fact]
    public async Task TransitionAsync_ToActivatedWithNoProviderIdAttribute_ReturnsNotDispatchedWithNoFailure()
    {
        var repository = new InMemoryServiceOrderRepository();
        var order = ActivatableOrder("order-1");
        order.Attributes.Remove(BssOrderAttributes.ProviderIdKey);
        repository.Seed(order);
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeFalse();
        result.ActivationWebhookFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TransitionAsync_ToActivatedWithUnknownProviderId_ReturnsNotDispatchedWithNoFailure()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(ActivatableOrder("order-1", providerId: "UNKNOWN"));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeFalse();
        result.ActivationWebhookFailureReason.Should().BeNull();
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TransitionAsync_ToActivatedMissingOntAttributes_ReturnsFailureButStillPersistsActivatedStatus()
    {
        var repository = new InMemoryServiceOrderRepository();
        var order = ActivatableOrder("order-1");
        order.Attributes.Remove(FtthDropInstallChecklistAttributes.OntFsanKey);
        repository.Seed(order);
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeFalse();
        result.ActivationWebhookFailureReason.Should().NotBeNullOrEmpty();
        repository.Find("order-1")!.Status.Should().Be(FtthDropInstallStatus.Activated);
    }

    [Fact]
    public async Task TransitionAsync_DispatcherThrowsHttpRequestException_ReturnsFailureButStillPersistsActivatedStatus()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(ActivatableOrder("order-1"));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher(new HttpRequestException("boom"));
        var service = CreateService(repository, dispatcher);

        var result = await service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        result.ActivationWebhookDispatched.Should().BeFalse();
        result.ActivationWebhookFailureReason.Should().NotBeNullOrEmpty();
        repository.Find("order-1")!.Status.Should().Be(FtthDropInstallStatus.Activated);
    }

    [Fact]
    public Task TransitionAsync_UnknownOrderId_ThrowsKeyNotFoundException()
    {
        var repository = new InMemoryServiceOrderRepository();
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionAsync("missing-order", FtthDropInstallStatus.CtoPortTest, FtthOrderType);

        return act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TransitionAsync_StructurallyIllegalTransition_ThrowsAndLeavesOrderUnchanged()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(NewOrder("order-1", FtthDropInstallStatus.CrmSale));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        var service = CreateService(repository, dispatcher);

        var act = () => service.TransitionAsync("order-1", FtthDropInstallStatus.Activated, FtthOrderType);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        repository.Find("order-1")!.Status.Should().Be(FtthDropInstallStatus.CrmSale);
        dispatcher.CallCount.Should().Be(0);
    }
}
