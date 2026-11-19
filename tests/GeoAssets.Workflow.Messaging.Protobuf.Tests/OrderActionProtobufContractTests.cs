using System.Reflection;
using FluentAssertions;
using GeoAssets.Workflow.Messaging.Protobuf.V1;
using GeoAssets.Workflow.Orders;
using Google.Protobuf;
using Xunit;

namespace GeoAssets.Workflow.Messaging.Protobuf.Tests;

/// <summary>
/// XD01-366 (AC a, Protobuf): the order action is stored in the action log and exchanged as JSON, but no Protobuf contract
/// carries one, so there is no Protobuf round trip to protect. This pins that fact: a message that gains an action field would
/// need the numeric-compatibility contract the JSON and the database have, and must be a deliberate change, not an accident.
/// </summary>
public sealed class OrderActionProtobufContractTests
{
    private static readonly Assembly ProtobufAssembly = typeof(IntegrationEnvelopeProto).Assembly;

    private static IEnumerable<Type> GeneratedMessages() =>
        ProtobufAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IMessage).IsAssignableFrom(t));

    [Fact]
    public void NoProtobufContractCarriesAnOrderAction()
    {
        var messages = GeneratedMessages().ToList();
        messages.Should().NotBeEmpty("the reflection must actually see the generated messages, or this test proves nothing");

        var fields = messages
            .SelectMany(m => m.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0
                    && !string.Equals(p.Name, "Descriptor", StringComparison.Ordinal)
                    && !string.Equals(p.Name, "Parser", StringComparison.Ordinal))
                .Select(p => (Message: m, Field: p)))
            .ToList();
        fields.Should().NotBeEmpty();

        fields.Where(f => TypesOf(f.Field.PropertyType).Any(t => t == typeof(OrderActionType) || t == typeof(OrderAction)))
            .Select(f => $"{f.Message.Name}.{f.Field.Name}")
            .Should().BeEmpty("no message field is typed as an order action");
        fields.Where(f => f.Field.Name.Contains("action", StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.Message.Name}.{f.Field.Name}")
            .Should().BeEmpty("no message field is named for an action");
    }

    [Fact]
    public void NoProtobufEnumIsAnOrderActionEnumeration()
    {
        var enums = ProtobufAssembly.GetTypes().Where(t => t.IsEnum).ToList();

        enums.Should().NotContain(t => t == typeof(OrderActionType));
        enums.Where(t => t.Name.Contains("action", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    private static IEnumerable<Type> TypesOf(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(TypesOf))
            {
                yield return argument;
            }
        }
    }
}
