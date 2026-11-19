using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Server.Tests.Routing;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;
using GeoAssets.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <c>AddServerTargetLedger</c> (XD01-379): the ledger's store, service and organization scope are always registered; the
/// consumer of the verticals' <c>OrderStateChangedV1</c> only when the host has a broker and its consumer section names
/// the topic to read — a host configured for publishing only keeps booting as before.
/// </summary>
public sealed class TargetLedgerHostWiringTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static ServiceCollection Register(params (string Key, string? Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddServerTargetLedger(Configuration(settings));
        return services;
    }

    private static bool IsConsumer(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IIntegrationEventHandler<OrderStateChangedEvent>)
        || (descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.IsGenericType == true
            && descriptor.ImplementationType.GetGenericArguments()[0] == typeof(OrderStateChangedEvent));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    public void AddServerTargetLedger_WithNoBroker_RegistersTheLedger_ButNoConsumer(string? transport)
    {
        var services = Register((WorkflowMessagingHostExtensions.OutboxTransportKey, transport), ("WorkflowKafkaConsumer:TopicName", "topic"));

        services.Should().ContainSingle(d => d.ServiceType == typeof(ITargetLedgerStore));
        services.Should().ContainSingle(d => d.ServiceType == typeof(TargetLedgerService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantOrganizationScope))
            .Which.ImplementationType.Should().Be<TenantOrganizationScope>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IWorkflowUnitOfWork));
        services.Where(IsConsumer).Should().BeEmpty("with no broker there is nothing to consume");
    }

    [Theory]
    [InlineData("Kafka")]
    [InlineData("ServiceBus")]
    public void AddServerTargetLedger_BrokerWithoutAConsumerTopic_RegistersNoConsumer(string transport)
    {
        var services = Register((WorkflowMessagingHostExtensions.OutboxTransportKey, transport));

        services.Where(IsConsumer).Should().BeEmpty("a host configured for publishing only keeps booting as before");
    }

    [Fact]
    public void AddServerTargetLedger_ServiceBusTopicWithoutASubscription_RegistersNoConsumer()
    {
        var services = Register(
            (WorkflowMessagingHostExtensions.OutboxTransportKey, "ServiceBus"), ("WorkflowServiceBusConsumer:TopicName", "orders"));

        services.Where(IsConsumer).Should().BeEmpty();
    }

    [Fact]
    public void AddServerTargetLedger_KafkaWithATopic_RegistersTheTerminalReleaseHandlerAndItsConsumer()
    {
        var services = Register(
            (WorkflowMessagingHostExtensions.OutboxTransportKey, "kafka"), (KafkaConsumerOptions.SectionName + ":TopicName", "orders"));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventHandler<OrderStateChangedEvent>))
            .Which.ImplementationType.Should().Be<TargetLedgerOrderStateChangedHandler>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(KafkaIntegrationEventConsumer<OrderStateChangedEvent>));
        services.Should().NotContain(d => d.ImplementationType == typeof(ServiceBusIntegrationEventConsumer<OrderStateChangedEvent>));
    }

    [Fact]
    public void AddServerTargetLedger_ServiceBusWithTopicAndSubscription_RegistersTheTerminalReleaseHandlerAndItsConsumer()
    {
        var services = Register(
            (WorkflowMessagingHostExtensions.OutboxTransportKey, "ServiceBus"),
            (ServiceBusConsumerOptions.SectionName + ":TopicName", "orders"),
            (ServiceBusConsumerOptions.SectionName + ":SubscriptionName", "kernel"));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventHandler<OrderStateChangedEvent>))
            .Which.ImplementationType.Should().Be<TargetLedgerOrderStateChangedHandler>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(ServiceBusIntegrationEventConsumer<OrderStateChangedEvent>));
        services.Should().NotContain(d => d.ImplementationType == typeof(KafkaIntegrationEventConsumer<OrderStateChangedEvent>));
    }

    [Fact]
    public void AddServerTargetLedger_AnExistingOrganizationScope_IsKept()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantOrganizationScope, OtherScope>();

        services.AddServerTargetLedger(Configuration());

        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantOrganizationScope))
            .Which.ImplementationType.Should().Be<OtherScope>();
    }

    [Fact]
    public void AddServerTargetLedger_NullArguments_Throw()
    {
        var nullServices = () => TargetLedgerHostExtensions.AddServerTargetLedger(null!, Configuration());
        var nullConfiguration = () => new ServiceCollection().AddServerTargetLedger(null!);

        nullServices.Should().Throw<ArgumentNullException>();
        nullConfiguration.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task KernelOnlyHost_WithNoModules_ResolvesTheLedger_RegistersNoConsumer_AndServesTheRoutesToServiceCallersOnly()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<TargetLedgerService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITargetLedgerStore>().Should().BeOfType<EFTargetLedgerStore>();
        scope.ServiceProvider.GetRequiredService<ITenantOrganizationScope>().OrganizationId.Should().BeNull();
        factory.Services.GetServices<IHostedService>().Select(h => h.GetType())
            .Should().NotContain(t => t.IsGenericType && t.GetGenericArguments()[0] == typeof(OrderStateChangedEvent));
        factory.Services.GetServices<IIntegrationEventHandler<OrderStateChangedEvent>>().Should().BeEmpty();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/targets/ledger/vertical-a/a1/release", UriKind.Relative), new { organizationId = Guid.NewGuid() });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the kernel-only test caller is a fully permitted user, not a service caller");
    }

    private sealed class OtherScope : ITenantOrganizationScope
    {
        public Guid? OrganizationId => null;

        public void Enter(Guid organizationId)
        {
        }
    }
}
