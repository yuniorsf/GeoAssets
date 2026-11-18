using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// This host's workflow integration-event transport (XD01-357): the outbox it stages events on and
/// which broker, if any, its relay publishes to.
/// </summary>
public sealed class WorkflowMessagingHostExtensionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none")]
    public void AddServerWorkflowMessaging_WithNoTransport_RegistersTheStoreAndTheSerializer_ButNoRelayAndNoPublisher(string? transport)
    {
        var services = new ServiceCollection();

        services.AddServerWorkflowMessaging(Configuration((WorkflowMessagingHostExtensions.OutboxTransportKey, transport)));

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
    public void AddServerWorkflowMessaging_WithATransport_RegistersItsPublisher_AndTheRelay(string transport, Type publisher)
    {
        var services = new ServiceCollection();

        services.AddServerWorkflowMessaging(Configuration((WorkflowMessagingHostExtensions.OutboxTransportKey, transport)));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher))
            .Which.ImplementationType.Should().Be(publisher);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService))
            .Which.ImplementationType.Should().Be<OutboxRelayProcessor>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxWriter));
    }

    [Fact]
    public void AddServerWorkflowMessaging_WithAnUnknownTransport_FailsAtStartup_NamingTheSettingAndWhatItAccepts()
    {
        var act = () => new ServiceCollection().AddServerWorkflowMessaging(Configuration((WorkflowMessagingHostExtensions.OutboxTransportKey, "RabbitMQ")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{WorkflowMessagingHostExtensions.OutboxTransportKey}*RabbitMQ*Kafka*ServiceBus*None*");
    }

    [Fact]
    public void AddServerWorkflowMessaging_SerializesWithProtocolBuffers()
    {
        var services = new ServiceCollection();
        services.AddServerWorkflowMessaging(Configuration());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IIntegrationMessageSerializer>().Should().BeOfType<ProtobufIntegrationMessageSerializer>();
    }

    [Fact]
    public void AddServerWorkflowMessaging_NullArguments_ThrowArgumentNullException()
    {
        ((Action)(() => WorkflowMessagingHostExtensions.AddServerWorkflowMessaging(null!, Configuration()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddServerWorkflowMessaging(null!))).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("None", false)]
    [InlineData("none", false)]
    [InlineData("Kafka", true)]
    [InlineData("ServiceBus", true)]
    public void HasBroker_IsTrueOnlyForANamedBroker(string? transport, bool expected)
    {
        WorkflowMessagingHostExtensions.HasBroker(Configuration((WorkflowMessagingHostExtensions.OutboxTransportKey, transport)))
            .Should().Be(expected);
        ((Action)(() => WorkflowMessagingHostExtensions.HasBroker(null!))).Should().Throw<ArgumentNullException>();
    }
}
