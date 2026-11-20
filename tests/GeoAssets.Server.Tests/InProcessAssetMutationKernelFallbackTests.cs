using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Server.Catalogs;
using GeoAssets.Server.Tests.Catalogs;
using GeoAssets.Workflow.Messaging.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-374: with no <see cref="IAssetMutationBatchApplier"/> registered (no provider pushdown), the in-process publisher applies
/// through <see cref="GeoAssets.Core.Services.CatalogMutationBatchApplier"/> over the <see cref="ICatalogPool"/> — the catalog
/// named by <c>GeoAssets:Catalog:InstanceId</c> when the pool has it, the default catalog otherwise — and a registered
/// applier takes precedence.
/// </summary>
public sealed class InProcessAssetMutationKernelFallbackTests
{
    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000374");

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();
    private readonly TestProviderPlugin _plugin = new();

    private async Task<ServiceProvider> BuildAsync(string? instanceId, IAssetMutationBatchApplier? registered = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{CatalogConfiguration.ProvidersKey}:0:Id"] = CatalogConfiguration.DefaultCatalogId,
            [$"{CatalogConfiguration.ProvidersKey}:0:Plugin"] = TestProviderPlugin.DefaultId,
            [$"{CatalogConfiguration.ProvidersKey}:1:Id"] = "west",
            [$"{CatalogConfiguration.ProvidersKey}:1:Plugin"] = TestProviderPlugin.DefaultId,
            [AssetCatalogConfiguration.InstanceIdKey] = instanceId,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(new GeoAssetsMeter("1.0.0"));
        services.AddSingleton<IProviderPlugin>(_plugin);
        services.AddServerCatalogPool(configuration);
        services.AddServerWorkflowMessaging(configuration);
        _doubles = KernelApplyDoubles.Replace(services);
        if (registered is not null)
            services.AddSingleton(registered);
        services.AddServerAssetMutationApply(configuration);

        var provider = services.BuildServiceProvider();
        await provider.InitializeCatalogPoolAsync();
        return provider;
    }

    private KernelApplyDoubles _doubles = null!;

    private static AssetMutationBatchRequestedEvent Batch(string catalogInstanceId) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: catalogInstanceId,
            organizationId: Organization,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations:
            [
                new AssetFeatureMutationItem(
                    "pole-1", AssetMutationKind.UpsertFeature, "Name", "pole", string.Empty, string.Empty, null, [],
                    new Dictionary<string, string>(StringComparer.Ordinal)),
            ],
            timeProvider: TimeProvider.System,
            batchId: Guid.NewGuid());

    private IntegrationEnvelope Envelope(AssetMutationBatchRequestedEvent batch) =>
        IntegrationEnvelope.Create(
            messageType: IntegrationEventTypes.AssetMutationBatchRequestedV1,
            schemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            partitionKey: "order-1",
            contentType: _serializer.ContentType,
            payload: _serializer.Serialize(batch),
            timeProvider: TimeProvider.System,
            catalogInstanceId: batch.CatalogInstanceId);

    [Fact]
    public async Task Publish_WithNoRegisteredApplier_AppliesToTheCatalogNamedByTheInstanceId()
    {
        await using var provider = await BuildAsync("west");

        await provider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(Envelope(Batch("west")));

        var pool = provider.GetRequiredService<ICatalogPool>();
        pool.GetCatalog("west").GetById("pole-1").Should().NotBeNull();
        pool.DefaultCatalog.GetById("pole-1").Should().BeNull();
        _doubles.Outbox.Messages.Should().ContainSingle().Which.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchAppliedV1);
    }

    [Fact]
    public async Task Publish_WithNoRegisteredApplier_AndAnInstanceIdThePoolLacks_AppliesToTheDefaultCatalog()
    {
        await using var provider = await BuildAsync("ghost");

        await provider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(Envelope(Batch("ghost")));

        var pool = provider.GetRequiredService<ICatalogPool>();
        pool.DefaultCatalog.GetById("pole-1").Should().NotBeNull();
        pool.GetCatalog("west").GetById("pole-1").Should().BeNull();
    }

    [Fact]
    public async Task Publish_WithNoRegisteredApplier_AndNoInstanceIdSetting_AppliesToTheDefaultCatalog()
    {
        await using var provider = await BuildAsync(instanceId: null);

        await provider.GetRequiredService<IIntegrationEventPublisher>()
            .PublishAsync(Envelope(Batch(AssetCatalogConfiguration.DefaultInstanceId)));

        var pool = provider.GetRequiredService<ICatalogPool>();
        pool.DefaultCatalog.GetById("pole-1").Should().NotBeNull();
        pool.GetCatalog("west").GetById("pole-1").Should().BeNull();
    }

    [Fact]
    public async Task Publish_WithARegisteredApplier_UsesItInsteadOfTheCatalog()
    {
        var recorder = new RecordingApplier();
        await using var provider = await BuildAsync("west", recorder);
        var batch = Batch("west");

        await provider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(Envelope(batch));

        recorder.Received.Should().ContainSingle().Which.BatchId.Should().Be(batch.BatchId);
        _doubles.Outbox.Messages.Should().ContainSingle().Which.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchAppliedV1);
        var pool = provider.GetRequiredService<ICatalogPool>();
        pool.GetCatalog("west").GetById("pole-1").Should().BeNull();
        pool.DefaultCatalog.GetById("pole-1").Should().BeNull();
    }

    private sealed class RecordingApplier : IAssetMutationBatchApplier
    {
        public List<AssetMutationBatchRequestedEvent> Received { get; } = [];

        public Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
        {
            Received.Add(batch);
            return Task.FromResult(AssetBatchAppliedEvent.Create(
                batchId: batch.BatchId,
                catalogInstanceId: batch.CatalogInstanceId,
                organizationId: batch.OrganizationId,
                requestedByService: batch.RequestedByService,
                appliedByUserId: batch.PerformedByUserId,
                appliedItems: [],
                timeProvider: TimeProvider.System));
        }
    }
}
