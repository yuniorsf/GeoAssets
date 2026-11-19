using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// JSON for <see cref="OrderAction"/> (XD01-366): reads the historical number (<see cref="OrderActionAliases"/>) or the code;
/// writes the number when the action has one — the exact bytes <see cref="OrderActionType"/> produced, so a reader that
/// predates the vocabulary still understands everything it understood before — and the code otherwise.
/// </summary>
public sealed class OrderActionJsonConverter : JsonConverter<OrderAction>
{
    public override OrderAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number when reader.TryGetInt32(out var number)
                                           && OrderActionAliases.TryFromNumber(number, out var aliased):
                return aliased;
            case JsonTokenType.String when reader.GetString() is { } code && !string.IsNullOrWhiteSpace(code):
                return new OrderAction(code);
            default:
                throw new JsonException($"Expected an order action number or code, found {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, OrderAction value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.IsEmpty) throw new JsonException("An empty order action cannot be written.");

        if (OrderActionAliases.NumberOf(value) is { } number)
            writer.WriteNumberValue(number);
        else
            writer.WriteStringValue(value.Code);
    }
}
