using System.Text.Json;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// IXC Soft adapter (XD01-280) — IXC is the priority BSS integration (&gt;50% market presence per
/// the ticket). <b>The exact webhook JSON field names below are NOT verified against IXC Soft's
/// real API documentation or a real sample payload — neither is available in this environment.</b>
/// They follow IXC Soft's commonly-referenced Portuguese snake_case field-naming convention
/// (<c>id_cliente</c>, <c>id_plano</c>, <c>endereco</c>, <c>fone</c>) as a best-effort, clearly-flagged
/// assumption. A reviewer with access to a real IXC Soft sandbox or sample webhook payload must
/// verify and correct these field names before this adapter is used against production traffic.
/// </summary>
public sealed class IxcSoftOrderAdapter : IBssOssOrderAdapter
{
    public string ProviderId => "IXC";

    public BssOrderSpawnSpec ParseOrderSpawn(string payloadJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new BssOrderSpawnParseException("IXC order-spawn payload is not well-formed JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            return new BssOrderSpawnSpec(
                SubscriberId: RequireString(root, "id_cliente"),
                SubscriberName: RequireString(root, "nome"),
                ServiceAddress: RequireString(root, "endereco"),
                PlanId: RequireString(root, "id_plano"),
                ContactPhone: OptionalString(root, "fone"),
                ContactEmail: OptionalString(root, "email"));
        }
    }

    public string BuildActivationPayload(IServiceOrder completedOrder)
    {
        ArgumentNullException.ThrowIfNull(completedOrder);

        var subscriberId = completedOrder.Attributes.GetValueOrDefault(BssOrderAttributes.SubscriberIdKey);
        var ontFsan = completedOrder.Attributes.GetValueOrDefault(FtthDropInstallChecklistAttributes.OntFsanKey);
        var ontMac = completedOrder.Attributes.GetValueOrDefault(FtthDropInstallChecklistAttributes.OntMacAddressKey);
        var ctoFeatureId = completedOrder.Attributes.GetValueOrDefault(FtthDropInstallChecklistAttributes.CtoFeatureIdKey);

        if (string.IsNullOrEmpty(subscriberId))
            throw new InvalidOperationException($"Order '{completedOrder.Id}' has no recorded {BssOrderAttributes.SubscriberIdKey} — cannot build an IXC activation callback without it.");

        if (string.IsNullOrEmpty(ontFsan) || string.IsNullOrEmpty(ontMac))
        {
            throw new InvalidOperationException(
                $"Order '{completedOrder.Id}' reached Activated without a recorded ONT FSAN/MAC — the drop-install checklist was not actually completed.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id_contrato", subscriberId);
            writer.WriteString("status", "ativo");
            writer.WriteString("onu_sn", ontFsan);
            writer.WriteString("onu_mac", ontMac);
            if (!string.IsNullOrEmpty(ctoFeatureId))
                writer.WriteString("cto_id", ctoFeatureId);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new BssOrderSpawnParseException($"IXC order-spawn payload is missing required field '{propertyName}'.");

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
