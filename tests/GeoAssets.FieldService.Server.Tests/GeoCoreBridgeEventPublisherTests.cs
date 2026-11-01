using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.FieldService.Server;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-229's AC: "Core outage simulated in a test causes retry with backoff, not message
/// loss" — <c>OutboxRelayProcessor</c>'s own retry/backoff loop (already covered elsewhere,
/// e.g. <c>EFOutboxAndInboxTests</c>) only engages if a failed publish actually throws rather
/// than swallowing the error, so this proves <see cref="GeoCoreBridgeEventPublisher"/> does
/// exactly that when the Core GeoAssets Server call fails.
/// </summary>
public class GeoCoreBridgeEventPublisherTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeIntegrationMessageSerializer : IIntegrationMessageSerializer
    {
        public string ContentType => "application/x-fake";
        public byte[] Serialize<TMessage>(TMessage message) where TMessage : notnull => [];

        public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> payload) where TMessage : notnull =>
            (TMessage)(object)_batch!;

        public AssetMutationBatchRequestedEvent? _batch;
    }

    private sealed class FakeGeoCoreClient : IGeoCoreClient
    {
        private readonly Exception? _throwOnExecute;
        public int ExecuteCallCount { get; private set; }

        public FakeGeoCoreClient(Exception? throwOnExecute = null) => _throwOnExecute = throwOnExecute;

        public Task<GeoAssets.Core.Models.GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GeoAssets.Core.Models.PagedResult<GeoAssets.Core.Models.GeoFeature>> QueryFeaturesAsync(GeoAssets.Core.Models.AssetQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default)
        {
            ExecuteCallCount++;
            if (_throwOnExecute is not null)
            {
                throw _throwOnExecute;
            }

            return Task.FromResult(AssetBatchAppliedEvent.Create(
                batch.BatchId, batch.CatalogInstanceId, batch.OrganizationId,
                batch.RequestedByService, batch.PerformedByUserId, [], new FakeTimeProvider()));
        }
    }

    private sealed class SingleClientResolver(IGeoCoreClient client) : IGeoCoreClientResolver
    {
        public IReadOnlyCollection<string> RegisteredCatalogIds => ["catalog-primary"];
        public IGeoCoreClient Resolve(string? catalogInstanceId = null, Guid organizationId = default) => client;
        public bool TryResolve(string? catalogInstanceId, Guid organizationId, [NotNullWhen(true)] out IGeoCoreClient? client_)
        {
            client_ = client;
            return true;
        }
    }

    private static IntegrationEnvelope Envelope(string messageType) => IntegrationEnvelope.Create(
        messageType: messageType,
        schemaVersion: 1,
        partitionKey: "order-1",
        contentType: "application/x-fake",
        payload: ReadOnlyMemory<byte>.Empty,
        timeProvider: new FakeTimeProvider());

    private static AssetMutationBatchRequestedEvent SampleBatch() => AssetMutationBatchRequestedEvent.Create(
        catalogInstanceId: "catalog-primary",
        organizationId: Org,
        requestedByService: "GeoAssets.FieldService.Server",
        performedByUserId: "inspector-1",
        mutations: [],
        timeProvider: new FakeTimeProvider());

    [Fact]
    public async Task PublishAsync_CoreReachable_CallsExecuteAssetDeltaBatchAsync()
    {
        var client = new FakeGeoCoreClient();
        var serializer = new FakeIntegrationMessageSerializer { _batch = SampleBatch() };
        var publisher = new GeoCoreBridgeEventPublisher(new SingleClientResolver(client), serializer);

        await publisher.PublishAsync(Envelope(IntegrationEventTypes.AssetMutationBatchRequestedV1));

        client.ExecuteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_CoreOutageSimulated_ThrowsRatherThanSwallowingTheFailure()
    {
        var client = new FakeGeoCoreClient(throwOnExecute: new HttpRequestException("Core GeoAssets Server unreachable"));
        var serializer = new FakeIntegrationMessageSerializer { _batch = SampleBatch() };
        var publisher = new GeoCoreBridgeEventPublisher(new SingleClientResolver(client), serializer);

        var act = async () => await publisher.PublishAsync(Envelope(IntegrationEventTypes.AssetMutationBatchRequestedV1));

        // The exception propagating uncaught is exactly what lets OutboxRelayProcessor's own
        // catch block mark the message failed-and-retry (with backoff) instead of treating a
        // failed publish as a silent success — see that type's TryDispatchPendingMessageAsync.
        await act.Should().ThrowAsync<HttpRequestException>();
        client.ExecuteCallCount.Should().Be(1, "the call was attempted — nothing was lost, it just failed");
    }

    [Fact]
    public async Task PublishAsync_UnsupportedMessageType_Throws()
    {
        var client = new FakeGeoCoreClient();
        var serializer = new FakeIntegrationMessageSerializer { _batch = SampleBatch() };
        var publisher = new GeoCoreBridgeEventPublisher(new SingleClientResolver(client), serializer);

        var act = async () => await publisher.PublishAsync(Envelope("some.other.message.v1"));

        await act.Should().ThrowAsync<NotSupportedException>();
        client.ExecuteCallCount.Should().Be(0);
    }
}
