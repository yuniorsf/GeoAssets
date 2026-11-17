using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// This host's wiring of the shared-use dispatch (XD01-316): the order types it registers, the
/// outbox it stages notifications on — and which broker, if any, its relay publishes to — and
/// the dispatch service with its unit of work and telemetry.
/// </summary>
public sealed class SharedUseHostExtensionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // ── Order types ───────────────────────────────────────────────────────────

    [Fact]
    public void RegisterSharedUseOrderTypes_WithNoPoleTypeConfigured_UsesTheSeededPoleType()
    {
        var registry = new OrderTypeRegistry();

        registry.RegisterSharedUseOrderTypes(Configuration());

        registry.Get(SharedUseOrderTypes.PoleWorkId).TargetPolicy!.TargetAssetTypeIds
            .Should().Equal(OrderOccupancyHostExtensions.SeededPoleAssetTypeId);
        registry.Get(SharedUseOrderTypes.OccupantNoticeId).TargetPolicy!.TargetAssetTypeIds
            .Should().Equal(OrderOccupancyHostExtensions.SeededPoleAssetTypeId);
    }

    [Fact]
    public void TheSeededPoleType_IsOneValue_ForThisHostAndForTheClientsThatRegisterTheSameTypes() =>
        SharedUseOrderTypes.SeededPoleAssetTypeId.Should().Be(OrderOccupancyHostExtensions.SeededPoleAssetTypeId);

    [Fact]
    public void RegisterSharedUseOrderTypes_UsesTheSamePoleTypesAsTheOccupancyRoutes()
    {
        var configuration = Configuration(
            ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", " wooden-pole "),
            ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:1", "concrete-pole"),
            ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:2", " "));
        var registry = new OrderTypeRegistry();

        registry.RegisterSharedUseOrderTypes(configuration);

        OrderOccupancyHostExtensions.PoleAssetTypeIds(configuration).Should().Equal("wooden-pole", "concrete-pole");
        registry.Get(SharedUseOrderTypes.PoleWorkId).TargetPolicy!.TargetAssetTypeIds.Should().Equal("wooden-pole", "concrete-pole");
    }

    [Fact]
    public void RegisterSharedUseOrderTypes_AndPoleAssetTypeIds_NullArguments_ThrowArgumentNullException()
    {
        ((Action)(() => SharedUseHostExtensions.RegisterSharedUseOrderTypes(null!, Configuration()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new OrderTypeRegistry().RegisterSharedUseOrderTypes(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OrderOccupancyHostExtensions.PoleAssetTypeIds(null!))).Should().Throw<ArgumentNullException>();
    }

    // ── Outbox ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none")]
    public void AddServerWorkflowOutbox_WithNoTransport_RegistersTheStoreAndTheSerializer_ButNoRelayAndNoPublisher(string? transport)
    {
        var services = new ServiceCollection();

        services.AddServerWorkflowOutbox(Configuration((SharedUseHostExtensions.OutboxTransportKey, transport)));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxWriter));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxDispatcherStore));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIdempotentInbox));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationMessageSerializer));
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService), "with nowhere to publish, staged messages wait in the table");
        services.Should().NotContain(d => d.ServiceType == typeof(IIntegrationEventPublisher));
    }

    [Theory]
    [InlineData("Kafka", typeof(KafkaIntegrationEventPublisher))]
    [InlineData("kafka", typeof(KafkaIntegrationEventPublisher))]
    [InlineData("ServiceBus", typeof(ServiceBusIntegrationEventPublisher))]
    [InlineData("SERVICEBUS", typeof(ServiceBusIntegrationEventPublisher))]
    public void AddServerWorkflowOutbox_WithATransport_RegistersItsPublisher_AndTheRelay(string transport, Type publisher)
    {
        var services = new ServiceCollection();

        services.AddServerWorkflowOutbox(Configuration((SharedUseHostExtensions.OutboxTransportKey, transport)));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher))
            .Which.ImplementationType.Should().Be(publisher);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService))
            .Which.ImplementationType.Should().Be<OutboxRelayProcessor>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxWriter));
    }

    [Fact]
    public void AddServerWorkflowOutbox_WithAnUnknownTransport_FailsAtStartup_NamingTheSettingAndWhatItAccepts()
    {
        var act = () => new ServiceCollection().AddServerWorkflowOutbox(Configuration((SharedUseHostExtensions.OutboxTransportKey, "RabbitMQ")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SharedUseHostExtensions.OutboxTransportKey}*RabbitMQ*Kafka*ServiceBus*None*");
    }

    [Fact]
    public void AddServerWorkflowOutbox_SerializesWithProtocolBuffers()
    {
        var services = new ServiceCollection();
        services.AddServerWorkflowOutbox(Configuration());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IIntegrationMessageSerializer>().Should().BeOfType<ProtobufIntegrationMessageSerializer>();
    }

    [Fact]
    public void AddServerWorkflowOutbox_AndAddServerSharedUseDispatch_NullArguments_ThrowArgumentNullException()
    {
        ((Action)(() => SharedUseHostExtensions.AddServerWorkflowOutbox(null!, Configuration()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddServerWorkflowOutbox(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SharedUseHostExtensions.AddServerSharedUseDispatch(null!))).Should().Throw<ArgumentNullException>();
    }

    // ── Dispatch service ──────────────────────────────────────────────────────

    [Fact]
    public void AddServerSharedUseDispatch_RegistersTheServiceBehindBothPorts_Decorated_WithTheEfUnitOfWork()
    {
        var services = new ServiceCollection();

        services.AddServerSharedUseDispatch();

        services.Should().ContainSingle(d => d.ServiceType == typeof(SharedUseDispatchService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseDispatchService))
            .Which.ImplementationFactory.Should().NotBeNull("the write port resolves to the span-and-metrics decorator");
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseDispatchPlanReader));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IWorkflowUnitOfWork))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped, "one unit of work per request, on that request's context");
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseFanOutTracer));
        typeof(EFWorkflowUnitOfWork).Should().Implement<IWorkflowUnitOfWork>();
    }
}
