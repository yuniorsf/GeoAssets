using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public class ObservableServiceOrderRepositoryTests
{
    static ObservableServiceOrderRepositoryTests()
    {
        // Without a registered listener, ActivitySource.StartActivity always returns null
        // (nothing is sampling) — same setup GeoAssetsActivitySourceTests uses.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == GeoAssetsActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    private static ServiceOrder Order(string id, string status = ServiceOrderStatus.Draft, string orderTypeId = "inspection")
        => new() { Id = id, Status = status, OrderTypeId = orderTypeId };

    private sealed class CapturingLogger : ILogger<ObservableServiceOrderRepository>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    /// <summary>Captures every span stopped on <see cref="GeoAssetsActivitySource.SourceName"/> while listening.</summary>
    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;
        public List<Activity> Activities { get; } = [];

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == GeoAssetsActivitySource.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Activities.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Captures every measurement recorded on the given meter/instrument while listening.</summary>
    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(long Value, KeyValuePair<string, object?>[] Tags)> Measurements { get; } = [];

        public MeasurementCapture(string meterName, string instrumentName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
                Measurements.Add((measurement, tags.ToArray())));
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private static (GeoAssetsActivitySource Tracer, GeoAssetsMeter Metrics, CapturingLogger Logger) Dependencies()
        => (new GeoAssetsActivitySource("1.0.0"), new GeoAssetsMeter("1.0.0"), new CapturingLogger());

    /// <summary>
    /// Full-featured <see cref="IServiceOrderRepository"/> test double, replacing the old
    /// <c>InMemoryServiceOrderRepository</c> (removed in XD01-129). Unlike the simpler fake in
    /// <c>GeoAssets.Workflow.Tests</c>, this one validates transitions itself — required here
    /// because <see cref="ObservableServiceOrderRepository"/> doesn't validate on its own; it
    /// relies entirely on its inner repository to throw <see cref="InvalidServiceOrderTransitionException"/>
    /// for the "illegal transition" tests below, the same contract the real
    /// <c>EFServiceOrderRepository</c> honors.
    /// </summary>
    private sealed class FakeServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, IServiceOrder> _store = [];

        public event EventHandler<IServiceOrder>? OrderAdded;
        public event EventHandler<IServiceOrder>? OrderUpdated;
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
        public event EventHandler<string>? OrderDeleted;

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_store.GetValueOrDefault(id));

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values]);

        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.IsRoot)]);

        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.ParentOrderId == parentId)]);

        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
        {
            var child = _store.GetValueOrDefault(childId);
            return Task.FromResult(child?.ParentOrderId is { } pid ? _store.GetValueOrDefault(pid) : null);
        }

        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.Status == status)]);

        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.AssignedTo == userId)]);

        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedBy == userId)]);

        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.OrderTypeId == orderTypeId)]);

        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to)]);

        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
            string targetId, DispatchTargetType targetType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>(
                [.. _store.Values.Where(o => o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType))]);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            _store[order.Id] = order;
            OrderAdded?.Invoke(this, order);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(order.Id, out var existing))
                throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

            var previous = existing.Status;
            if (!ServiceOrderTransitions.IsValid(previous, order.Status))
                throw new InvalidServiceOrderTransitionException(previous, order.Status);

            _store[order.Id] = order;

            OrderUpdated?.Invoke(this, order);
            if (previous != order.Status)
                OrderStatusChanged?.Invoke(this, (order, previous));

            return Task.CompletedTask;
        }

        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
        {
            var order = (ServiceOrder)_store[orderId];
            order.Dispatches.Add(dispatch);
            OrderUpdated?.Invoke(this, order);
            return Task.CompletedTask;
        }

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            var order = (ServiceOrder)_store[orderId];
            var previous = order.Status;

            if (entry.ResultingStatus is not null && !ServiceOrderTransitions.IsValid(previous, entry.ResultingStatus))
                throw new InvalidServiceOrderTransitionException(previous, entry.ResultingStatus);

            order.ActionLog.Add(entry);
            if (entry.ResultingStatus is not null)
                order.Status = entry.ResultingStatus;

            OrderUpdated?.Invoke(this, order);
            if (entry.ResultingStatus is not null && entry.ResultingStatus != previous)
                OrderStatusChanged?.Invoke(this, (order, previous));

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _store.Remove(id);
            OrderDeleted?.Invoke(this, id);
            return Task.CompletedTask;
        }
    }

    // ── Read / query pass-through ────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByIdAsync("a"))!.Id.Should().Be("a");
    }

    [Fact]
    public async Task GetAllAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetRootsAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetRootsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetChildrenAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("parent"));
        await inner.AddAsync(new ServiceOrder { Id = "child", ParentOrderId = "parent" });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetChildrenAsync("parent")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetParentAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("parent"));
        await inner.AddAsync(new ServiceOrder { Id = "child", ParentOrderId = "parent" });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetParentAsync("child"))!.Id.Should().Be("parent");
    }

    [Fact]
    public async Task GetByStatusAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Pending));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByStatusAsync(ServiceOrderStatus.Pending)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByAssigneeAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", AssignedTo = "tech-1" });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByAssigneeAsync("tech-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByCreatorAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", CreatedBy = "alice" });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByCreatorAsync("alice")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByOrderTypeAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", OrderTypeId = "inspection" });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByOrderTypeAsync("inspection")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByDateRangeAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", CreatedAt = new DateTime(2026, 1, 15) });
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Should().ContainSingle();
    }

    [Fact]
    public async Task GetDispatchedToAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1", TimeProvider.System));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        (await sut.GetDispatchedToAsync("user-1", DispatchTargetType.User)).Should().ContainSingle();
    }

    // ── Write pass-through (non-transition) ──────────────────────────────────

    [Fact]
    public async Task AddAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        await sut.AddAsync(Order("a"));

        (await inner.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task AppendDispatchAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        await sut.AppendDispatchAsync("a", new OrderDispatch("user-1", DispatchTargetType.User, "supervisor-1", DateTime.UtcNow));

        (await inner.GetByIdAsync("a"))!.Dispatches.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInner()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);

        await sut.DeleteAsync("a");

        (await inner.GetByIdAsync("a")).Should().BeNull();
    }

    // ── Events (forwarded, add and remove) ───────────────────────────────────

    [Fact]
    public async Task OrderAdded_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new FakeServiceOrderRepository();
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        var count = 0;
        EventHandler<IServiceOrder> handler = (_, _) => count++;

        sut.OrderAdded += handler;
        await sut.AddAsync(Order("a"));
        count.Should().Be(1);

        sut.OrderAdded -= handler;
        await sut.AddAsync(Order("b"));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderUpdated_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        var count = 0;
        EventHandler<IServiceOrder> handler = (_, _) => count++;

        sut.OrderUpdated += handler;
        await sut.UpdateAsync(Order("a"));
        count.Should().Be(1);

        sut.OrderUpdated -= handler;
        await sut.UpdateAsync(Order("a"));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderStatusChanged_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        var count = 0;
        EventHandler<(IServiceOrder Order, string Previous)> handler = (_, _) => count++;

        sut.OrderStatusChanged += handler;
        await sut.UpdateAsync(Order("a", ServiceOrderStatus.Pending));
        count.Should().Be(1);

        sut.OrderStatusChanged -= handler;
        await sut.UpdateAsync(Order("a", ServiceOrderStatus.InProgress));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderDeleted_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        await inner.AddAsync(Order("b"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        var count = 0;
        EventHandler<string> handler = (_, _) => count++;

        sut.OrderDeleted += handler;
        await sut.DeleteAsync("a");
        count.Should().Be(1);

        sut.OrderDeleted -= handler;
        await sut.DeleteAsync("b");
        count.Should().Be(1);
    }

    // ── UpdateAsync transition instrumentation ───────────────────────────────

    [Fact]
    public async Task UpdateAsync_StatusChanges_RecordsSpanAndMetric()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft, orderTypeId: "inspection"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        using var activities = new ActivityCapture();
        using var measurements = new MeasurementCapture(GeoAssetsMeter.MeterName, "geoassets.orders.transitions");

        await sut.UpdateAsync(Order("a", ServiceOrderStatus.Pending, orderTypeId: "inspection"));

        activities.Activities.Should().ContainSingle();
        activities.Activities[0].GetTagItem("order.id").Should().Be("a");
        activities.Activities[0].GetTagItem("order.type").Should().Be("inspection");
        activities.Activities[0].GetTagItem("order.prev_status").Should().Be(ServiceOrderStatus.Draft);
        activities.Activities[0].GetTagItem("order.new_status").Should().Be(ServiceOrderStatus.Pending);

        measurements.Measurements.Should().ContainSingle();
        var (value, tags) = measurements.Measurements[0];
        value.Should().Be(1);
        tags.Should().Contain(new KeyValuePair<string, object?>("order.type", "inspection"));
        tags.Should().Contain(new KeyValuePair<string, object?>("order.prev_status", ServiceOrderStatus.Draft));
        tags.Should().Contain(new KeyValuePair<string, object?>("order.new_status", ServiceOrderStatus.Pending));
    }

    [Fact]
    public async Task UpdateAsync_StatusUnchanged_RecordsNothing()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        using var activities = new ActivityCapture();
        using var measurements = new MeasurementCapture(GeoAssetsMeter.MeterName, "geoassets.orders.transitions");

        await sut.UpdateAsync(Order("a", ServiceOrderStatus.Draft));

        activities.Activities.Should().BeEmpty();
        measurements.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_IllegalTransition_LogsWarningAndRethrows_RecordsNothing()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        using var activities = new ActivityCapture();
        using var measurements = new MeasurementCapture(GeoAssetsMeter.MeterName, "geoassets.orders.transitions");

        var act = () => sut.UpdateAsync(Order("a", ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        activities.Activities.Should().BeEmpty();
        measurements.Measurements.Should().BeEmpty();

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain("a").And.Contain("Draft").And.Contain("Completed");
        logger.Entries[0].Exception.Should().BeOfType<InvalidServiceOrderTransitionException>();
    }

    // ── AppendActionAsync transition instrumentation ─────────────────────────

    [Fact]
    public async Task AppendActionAsync_ResultingStatus_RecordsSpanAndMetric()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft, orderTypeId: "inspection"));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        using var activities = new ActivityCapture();
        using var measurements = new MeasurementCapture(GeoAssetsMeter.MeterName, "geoassets.orders.transitions");

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Approve, "supervisor-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Pending));

        activities.Activities.Should().ContainSingle();
        measurements.Measurements.Should().ContainSingle();
        var (value, tags) = measurements.Measurements[0];
        value.Should().Be(1);
        tags.Should().Contain(new KeyValuePair<string, object?>("order.type", "inspection"));
        tags.Should().Contain(new KeyValuePair<string, object?>("order.prev_status", ServiceOrderStatus.Draft));
        tags.Should().Contain(new KeyValuePair<string, object?>("order.new_status", ServiceOrderStatus.Pending));
    }

    [Fact]
    public async Task AppendActionAsync_IllegalResultingStatus_LogsWarningAndRethrows_RecordsNothing()
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var (tracer, metrics, logger) = Dependencies();
        var sut = new ObservableServiceOrderRepository(inner, tracer, metrics, logger);
        using var activities = new ActivityCapture();
        using var measurements = new MeasurementCapture(GeoAssetsMeter.MeterName, "geoassets.orders.transitions");

        var act = () => sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        activities.Activities.Should().BeEmpty();
        measurements.Measurements.Should().BeEmpty();

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain("a").And.Contain("Draft").And.Contain("Completed");
    }
}
