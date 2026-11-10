using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Erp.Sap;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sap;

/// <summary>
/// XD01-231's AC2 (idempotency-token deduplication on webhook retry), functional-location
/// resolution, and SLA-deadline attachment, for <see cref="SapPmOrderSpawnService"/>.
/// </summary>
public class SapPmOrderSpawnServiceTests
{
    private const string OrderTypeId = "field-work";
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class RecordingServiceOrderWriter : IServiceOrderWriter
    {
        public List<ServiceOrder> AddedOrders { get; } = [];

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            AddedOrders.Add((ServiceOrder)order);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }

    private sealed class FakeGeoCoreClient(GeoFeature? feature) : IGeoCoreClient
    {
        public int CallCount { get; private set; }
        public string? LastRequestedId { get; private set; }

        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default)
        {
            CallCount++;
            LastRequestedId = id;
            return Task.FromResult(feature);
        }

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeIdempotentInbox : IIdempotentInbox
    {
        private readonly HashSet<(Guid MessageId, string ConsumerName)> _processed = [];

        public List<(Guid MessageId, string ConsumerName, string MessageType, TimeSpan RetentionPeriod)> RecordedEntries { get; } = [];

        public Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default) =>
            Task.FromResult(_processed.Contains((messageId, consumerName)));

        public Task<bool> TryRecordProcessedAsync(
            Guid messageId, string consumerName, string messageType, TimeSpan retentionPeriod, CancellationToken ct = default)
        {
            if (!_processed.Add((messageId, consumerName)))
                return Task.FromResult(false);

            RecordedEntries.Add((messageId, consumerName, messageType, retentionPeriod));
            return Task.FromResult(true);
        }

        public Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SapPmOrderSpawnSpec ValidSpec(Guid? notificationId = null) => new(
        FunctionalLocation: "floc-001",
        OrderNumber: "4500001234",
        ShortText: "Replace damaged pole",
        ServiceCategory: "EmergencyRestoration",
        TerritorialSetting: "Urban",
        NotificationId: notificationId ?? Guid.NewGuid(),
        ReportedAtUtc: FixedNow);

    private static SlaGuardEngine BuildSlaGuard() => new([new SlaMatrixRow("EmergencyRestoration", "Urban", TimeSpan.FromHours(1))]);

    private static SapPmOrderSpawnService CreateService(
        RecordingServiceOrderWriter writer, FakeGeoCoreClient geoCore, FakeIdempotentInbox inbox, TimeProvider? timeProvider = null) =>
        new(writer, geoCore, inbox, BuildSlaGuard(), timeProvider ?? new FakeTimeProvider(FixedNow));

    [Fact]
    public async Task SpawnFromNotificationAsync_HappyPath_PersistsPendingOrderWithAttributesAndSlaDeadline()
    {
        var writer = new RecordingServiceOrderWriter();
        var feature = new GeoFeature { Id = "feature-1" };
        var geoCore = new FakeGeoCoreClient(feature);
        var inbox = new FakeIdempotentInbox();
        var spec = ValidSpec();
        var service = CreateService(writer, geoCore, inbox);

        var result = await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);

        result.AlreadyProcessed.Should().BeFalse();
        result.Order.Should().NotBeNull();
        var order = result.Order!;
        order.Status.Should().Be(ServiceOrderStatus.Pending);
        order.OrderTypeId.Should().Be(OrderTypeId);
        order.Title.Should().Be(spec.ShortText);
        order.OrganizationId.Should().Be(OrganizationId);
        order.FeatureIds.Should().ContainSingle().Which.Should().Be("feature-1");
        order.Attributes[SapPmOrderAttributes.FunctionalLocationKey].Should().Be(spec.FunctionalLocation);
        order.Attributes[SapPmOrderAttributes.OrderNumberKey].Should().Be(spec.OrderNumber);
        order.Attributes[SapPmOrderAttributes.NotificationIdKey].Should().Be(spec.NotificationId.ToString());
        order.Attributes.Should().ContainKey(SlaGuardEngine.DeadlineAttributeKey);
        order.Attributes[SlaGuardEngine.ServiceCategoryAttributeKey].Should().Be(spec.ServiceCategory);
        order.Attributes[SlaGuardEngine.TerritorialSettingAttributeKey].Should().Be(spec.TerritorialSetting);
        writer.AddedOrders.Should().ContainSingle();
        geoCore.LastRequestedId.Should().Be(spec.FunctionalLocation);
    }

    [Fact]
    public async Task SpawnFromNotificationAsync_DuplicateNotificationId_ReturnsAlreadyProcessedWithoutCallingGeoCoreOrWriter()
    {
        var writer = new RecordingServiceOrderWriter();
        var geoCore = new FakeGeoCoreClient(new GeoFeature { Id = "feature-1" });
        var inbox = new FakeIdempotentInbox();
        var spec = ValidSpec();
        var service = CreateService(writer, geoCore, inbox);

        var first = await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);
        var second = await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);

        first.AlreadyProcessed.Should().BeFalse();
        second.AlreadyProcessed.Should().BeTrue();
        second.Order.Should().BeNull();
        geoCore.CallCount.Should().Be(1);
        writer.AddedOrders.Should().ContainSingle();
    }

    [Fact]
    public async Task SpawnFromNotificationAsync_UnknownFunctionalLocation_ThrowsSapPmFunctionalLocationNotFoundException()
    {
        var writer = new RecordingServiceOrderWriter();
        var geoCore = new FakeGeoCoreClient(feature: null);
        var inbox = new FakeIdempotentInbox();
        var spec = ValidSpec();
        var service = CreateService(writer, geoCore, inbox);

        var act = async () => await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);

        await act.Should().ThrowAsync<SapPmFunctionalLocationNotFoundException>().WithMessage($"*{spec.FunctionalLocation}*");
        writer.AddedOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task SpawnFromNotificationAsync_RetryAfterFunctionalLocationNotFound_SucceedsOnceLocationIsResolvable()
    {
        // The idempotency key is only recorded AFTER a fully successful spawn, deliberately: a
        // webhook retry for a notification whose location lookup failed once (bad/missing
        // geo_entity data at the time) must still be able to succeed once that data is fixed,
        // rather than being stuck "already processed" for a notification that never spawned
        // anything.
        var writer = new RecordingServiceOrderWriter();
        var geoCoreMissing = new FakeGeoCoreClient(feature: null);
        var inbox = new FakeIdempotentInbox();
        var spec = ValidSpec();
        var firstAttemptService = CreateService(writer, geoCoreMissing, inbox);

        await Assert.ThrowsAsync<SapPmFunctionalLocationNotFoundException>(
            () => firstAttemptService.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId));
        writer.AddedOrders.Should().BeEmpty();

        var geoCoreNowResolvable = new FakeGeoCoreClient(new GeoFeature { Id = "feature-1" });
        var retryService = CreateService(writer, geoCoreNowResolvable, inbox);
        var retry = await retryService.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);

        retry.AlreadyProcessed.Should().BeFalse();
        retry.Order.Should().NotBeNull();
        writer.AddedOrders.Should().ContainSingle();
    }

    [Fact]
    public async Task SpawnFromNotificationAsync_RetryAfterSuccessfulSpawn_ReturnsAlreadyProcessedWithoutSpawningAgain()
    {
        var writer = new RecordingServiceOrderWriter();
        var geoCore = new FakeGeoCoreClient(new GeoFeature { Id = "feature-1" });
        var inbox = new FakeIdempotentInbox();
        var spec = ValidSpec();
        var service = CreateService(writer, geoCore, inbox);

        await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);
        var retry = await service.SpawnFromNotificationAsync(spec, OrderTypeId, OrganizationId);

        retry.AlreadyProcessed.Should().BeTrue();
        retry.Order.Should().BeNull();
        writer.AddedOrders.Should().ContainSingle();
    }

    [Fact]
    public Task SpawnFromNotificationAsync_NullSpec_ThrowsArgumentNullException()
    {
        var writer = new RecordingServiceOrderWriter();
        var geoCore = new FakeGeoCoreClient(new GeoFeature { Id = "feature-1" });
        var inbox = new FakeIdempotentInbox();
        var service = CreateService(writer, geoCore, inbox);

        var act = () => service.SpawnFromNotificationAsync(null!, OrderTypeId, OrganizationId);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public Task SpawnFromNotificationAsync_EmptyOrderTypeId_ThrowsArgumentException()
    {
        var writer = new RecordingServiceOrderWriter();
        var geoCore = new FakeGeoCoreClient(new GeoFeature { Id = "feature-1" });
        var inbox = new FakeIdempotentInbox();
        var service = CreateService(writer, geoCore, inbox);

        var act = () => service.SpawnFromNotificationAsync(ValidSpec(), string.Empty, OrganizationId);

        return act.Should().ThrowAsync<ArgumentException>();
    }
}
