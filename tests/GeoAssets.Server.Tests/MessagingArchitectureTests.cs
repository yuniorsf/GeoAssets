using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Provider.Rest;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Server.Tests;

[Trait("Category", "Architecture")]
public sealed class MessagingArchitectureTests
{
    private static readonly string[] ForbiddenDomainAndConcessionaireTokens =
    [
        "IpBilling",
        "PublicLighting",
        "Luminaire",
        "Coelba",
        "Equatorial",
        "Aguas",
        "Sabesp",
        "Neoenergia",
        "Cemig",
        "Copel",
        "Celesc",
        "Concessionaire",
        "Sanitation"
    ];

    [Fact]
    public void Invariant01_CoreSdkAssemblies_NeverReferenceBrokerPersistenceProtobufOrServerInfrastructure()
    {
        Assembly[] coreAssemblies =
        [
            typeof(IntegrationEnvelope).Assembly,
            typeof(OrderStateChangedEvent).Assembly
        ];

        string[] forbiddenPrefixes =
        [
            "Confluent.Kafka",
            "Azure.Messaging.ServiceBus",
            "Google.Protobuf",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "GeoAssets.Provider",
            "GeoAssets.Workflow.EFCore",
            "GeoAssets.Workflow.Messaging",
            "GeoAssets.Server",
            "GeoAssets.IpBilling"
        ];

        foreach (var assembly in coreAssemblies)
        {
            var referenced = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .ToList();

            foreach (var prefix in forbiddenPrefixes)
            {
                referenced.Should().NotContain(
                    r => r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                    $"core SDK assembly '{assembly.GetName().Name}' must not reference '{prefix}'");
            }
        }

        var workflowReferenced = typeof(OrderStateChangedEvent).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        workflowReferenced.Should().NotContain(
            r => r.StartsWith("NetTopologySuite", StringComparison.OrdinalIgnoreCase),
            "workflow core SDK assembly 'GeoAssets.Workflow' must not reference 'NetTopologySuite'");
    }

    [Fact]
    public void Invariant02_PlatformCoreAndMessagingAssemblies_Remain100PercentDomainAndConcessionaireAgnostic()
    {
        Assembly[] agnosticAssemblies =
        [
            typeof(IntegrationEnvelope).Assembly,
            typeof(OrderStateChangedEvent).Assembly,
            typeof(FederatedGeoCoreClientResolver).Assembly,
            typeof(ServiceOrderDbContext).Assembly,
            typeof(ProtobufIntegrationMessageSerializer).Assembly,
            typeof(KafkaOrderEventPublisher).Assembly,
            typeof(ServiceBusOrderEventPublisher).Assembly
        ];

        var violations = new List<string>();

        foreach (var assembly in agnosticAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (IsCompilerGenerated(type.Name))
                {
                    continue;
                }

                CheckIdentifier(violations, assembly, type.FullName ?? type.Name);

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (IsCompilerGenerated(member.Name))
                    {
                        continue;
                    }

                    CheckIdentifier(violations, assembly, $"{type.Name}.{member.Name}");

                    if (member is MethodBase method)
                    {
                        foreach (var parameter in method.GetParameters())
                        {
                            if (parameter.Name is not null)
                            {
                                CheckIdentifier(violations, assembly, $"{type.Name}.{member.Name}({parameter.Name})");
                            }
                        }
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "platform core, provider, outbox, and messaging assemblies must remain 100% vertical-domain and concessionaire agnostic");
    }

    [Fact]
    public void Invariant03_BrokerTransportAndHandlerPorts_ProhibitTaskOfTResponseRpcOverBroker()
    {
        Type[] brokerPorts =
        [
            typeof(IIntegrationEventPublisher),
            typeof(IIntegrationEventHandler<>)
        ];

        foreach (var port in brokerPorts)
        {
            foreach (var method in port.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var returnType = method.ReturnType;
                var isGenericTaskResponse = returnType.IsGenericType
                    && (returnType.GetGenericTypeDefinition() == typeof(Task<>)
                        || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

                isGenericTaskResponse.Should().BeFalse(
                    $"broker port '{port.Name}.{method.Name}' must be asynchronous fire-and-forget (Task), never RPC-over-broker Task<TResponse>");
                returnType.Should().Be<Task>();
            }
        }
    }

    [Fact]
    public void Invariant04_CoreMessagingContracts_HaveZeroNetTopologySuiteOrGoogleProtobufLeakage()
    {
        var coreAssembly = typeof(IntegrationEnvelope).Assembly;
        var messagingTypes = coreAssembly.GetTypes()
            .Where(t => t.Namespace is not null
                && (string.Equals(t.Namespace, "GeoAssets.Core.Messaging", StringComparison.Ordinal)
                    || t.Namespace.StartsWith("GeoAssets.Core.Messaging.", StringComparison.Ordinal)))
            .ToList();

        messagingTypes.Should().NotBeEmpty();
        messagingTypes.Should().Contain(typeof(AssetMutationBatchRequestedEvent));
        messagingTypes.Should().Contain(typeof(FeatureCatalogMutatedEvent));

        foreach (var type in messagingTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propNs = prop.PropertyType.Namespace ?? string.Empty;
                propNs.Should().NotStartWith("NetTopologySuite");
                propNs.Should().NotStartWith("Google.Protobuf");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var retNs = method.ReturnType.Namespace ?? string.Empty;
                retNs.Should().NotStartWith("NetTopologySuite");
                retNs.Should().NotStartWith("Google.Protobuf");

                foreach (var param in method.GetParameters())
                {
                    var paramNs = param.ParameterType.Namespace ?? string.Empty;
                    paramNs.Should().NotStartWith("NetTopologySuite");
                    paramNs.Should().NotStartWith("Google.Protobuf");
                }
            }
        }
    }

    [Fact]
    public void Invariant05_InterfaceSegregation_AllMessagingAndFederationPortsDeclareAtMostFiveMethods()
    {
        var coreAssembly = typeof(IntegrationEnvelope).Assembly;
        var ports = coreAssembly.GetTypes()
            .Where(t => t.IsInterface
                && (string.Equals(t.Namespace, "GeoAssets.Core.Messaging", StringComparison.Ordinal)
                    || t == typeof(IGeoCoreClientResolver)))
            .ToList();

        ports.Should().Contain(typeof(IGeoCoreClientResolver));
        ports.Should().Contain(typeof(IIntegrationMessageSerializer));
        ports.Should().Contain(typeof(IIntegrationEventPublisher));
        ports.Should().Contain(typeof(IOutboxWriter));
        ports.Should().Contain(typeof(IOutboxDispatcherStore));
        ports.Should().Contain(typeof(IIdempotentInbox));

        foreach (var port in ports)
        {
            var declaredMethods = port.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .ToList();

            declaredMethods.Count.Should().BeLessThanOrEqualTo(
                5,
                $"ISP requires interface '{port.FullName}' to declare at most 5 methods");
        }
    }

    [Fact]
    public void Invariant06_AsyncMethodsOnMessagingAndFederationPorts_AcceptCancellationTokenAsFinalParameter()
    {
        var coreAssembly = typeof(IntegrationEnvelope).Assembly;
        var ports = coreAssembly.GetTypes()
            .Where(t => t.IsInterface
                && (string.Equals(t.Namespace, "GeoAssets.Core.Messaging", StringComparison.Ordinal)
                    || t == typeof(IGeoCoreClientResolver)
                    || t == typeof(IGeoCoreClient)))
            .ToList();

        foreach (var port in ports)
        {
            foreach (var method in port.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsAsyncReturnType(method.ReturnType))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                parameters.Should().NotBeEmpty(
                    $"async method '{port.Name}.{method.Name}' must accept a trailing CancellationToken");
                parameters[^1].ParameterType.Should().Be<CancellationToken>(
                    $"async method '{port.Name}.{method.Name}' must accept CancellationToken as its final parameter");
            }
        }
    }

    [Fact]
    public void Invariant07_ProtobufAdapter_RemainsIsolatedFromBrokerTransportsAndEfCore()
    {
        var protobufAssembly = typeof(ProtobufIntegrationMessageSerializer).Assembly;
        var referenced = protobufAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        string[] forbidden =
        [
            "Confluent.Kafka",
            "Azure.Messaging.ServiceBus",
            "Microsoft.EntityFrameworkCore",
            "GeoAssets.Workflow.Messaging.Kafka",
            "GeoAssets.Workflow.Messaging.ServiceBus",
            "GeoAssets.Workflow.EFCore",
            "GeoAssets.Provider"
        ];

        foreach (var prefix in forbidden)
        {
            referenced.Should().NotContain(
                r => r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                $"GeoAssets.Workflow.Messaging.Protobuf must not depend on '{prefix}'");
        }
    }

    [Fact]
    public void Invariant08_GeneratedProtobufMessages_RemainStrictlyInternalEncapsulationDetails()
    {
        var protobufAssembly = typeof(ProtobufIntegrationMessageSerializer).Assembly;
        var protoMessages = protobufAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => string.Equals(i.FullName, "Google.Protobuf.IMessage", StringComparison.Ordinal)))
            .ToList();

        protoMessages.Should().NotBeEmpty("Grpc.Tools must generate internal IMessage wire classes from *.proto");

        foreach (var messageType in protoMessages)
        {
            (messageType.IsPublic || messageType.IsNestedPublic).Should().BeFalse(
                $"generated Protobuf wire type '{messageType.FullName}' must have internal visibility");
        }
    }

    [Fact]
    public void Invariant09_SiblingBrokerAndRestAdapters_NeverCrossReferenceEachOtherOrProtobufConcreteAdapter()
    {
        var kafkaRefs = typeof(KafkaOrderEventPublisher).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        kafkaRefs.Should().NotContain("GeoAssets.Workflow.Messaging.ServiceBus");
        kafkaRefs.Should().NotContain("Azure.Messaging.ServiceBus");
        kafkaRefs.Should().NotContain("GeoAssets.Workflow.Messaging.Protobuf");
        kafkaRefs.Should().NotContain(r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        var serviceBusRefs = typeof(ServiceBusOrderEventPublisher).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        serviceBusRefs.Should().NotContain("GeoAssets.Workflow.Messaging.Kafka");
        serviceBusRefs.Should().NotContain("Confluent.Kafka");
        serviceBusRefs.Should().NotContain("GeoAssets.Workflow.Messaging.Protobuf");
        serviceBusRefs.Should().NotContain(r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        var restRefs = typeof(FederatedGeoCoreClientResolver).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        restRefs.Should().NotContain("Confluent.Kafka");
        restRefs.Should().NotContain("Azure.Messaging.ServiceBus");
        restRefs.Should().NotContain("Google.Protobuf");
        restRefs.Should().NotContain(r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariant10_VerticalMicroserviceAndCoreServer_EnforceDatabaseAndAssemblyDecoupling()
    {
        var coreServerAssembly = Assembly.Load("GeoAssets.Server");
        var ipBillingServerAssembly = Assembly.Load("GeoAssets.IpBilling.Server");

        var coreServerRefs = coreServerAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();
        coreServerRefs.Should().NotContain(
            r => r.StartsWith("GeoAssets.IpBilling", StringComparison.OrdinalIgnoreCase),
            "Core GIS Server must remain decoupled from vertical domain microservices");

        var ipBillingRefs = ipBillingServerAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();
        ipBillingRefs.Should().NotContain(
            "GeoAssets.Server",
            "Vertical IpBilling Server must communicate via REST reads and broker events, never direct assembly reference to GeoAssets.Server");
        ipBillingRefs.Should().NotContain(
            "GeoAssets.Provider.PostgreSQL",
            "Vertical IpBilling Server must never access Core GIS PostGIS provider directly");
        ipBillingRefs.Should().NotContain(
            r => r.StartsWith("NetTopologySuite", StringComparison.OrdinalIgnoreCase),
            "Vertical IpBilling Server must remain decoupled from NetTopologySuite spatial database types");
    }

    private static bool IsAsyncReturnType(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            return true;
        }

        if (!returnType.IsGenericType)
        {
            return false;
        }

        var genericDef = returnType.GetGenericTypeDefinition();
        return genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>);
    }

    private static bool IsCompilerGenerated(string name) =>
        name.Contains('<', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal);

    private static void CheckIdentifier(List<string> violations, Assembly assembly, string identifier)
    {
        foreach (var token in ForbiddenDomainAndConcessionaireTokens)
        {
            if (identifier.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"[{assembly.GetName().Name}] '{identifier}' contains forbidden token '{token}'");
            }
        }
    }
}
