using System.Globalization;
using System.Text.Json;

namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// SAP PM/S4HANA OData/Event Mesh work-order-notification adapter (XD01-231). <b>The exact field
/// names below are NOT verified against a real SAP sandbox or sample payload — neither is
/// available in this environment.</b> <c>FunctionalLocation</c>, <c>OrderNumber</c> and
/// <c>ShortText</c> follow SAP PM's standard OData camelCase field naming (functional location,
/// order number, short text); <c>ServiceCategory</c>/<c>TerritorialSetting</c> are modeled as
/// customer Z-fields carrying this repo's own <see cref="SlaGuard.SlaGuardEngine"/> matrix keys;
/// <c>NotificationId</c> follows SAP Event Mesh's CloudEvents message-id convention, used here as
/// the webhook-retry idempotency token. A reviewer with access to a real SAP sandbox or sample
/// payload must verify and correct these field names before this adapter is used against
/// production traffic — same caveat this repo's IXC Soft BSS/OSS adapter already carries.
/// </summary>
public sealed class SapPmOrderAdapter : ISapPmOrderAdapter
{
    public SapPmOrderSpawnSpec ParseWorkOrderNotification(string payloadJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new SapPmOrderParseException("SAP PM work-order notification payload is not well-formed JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            return new SapPmOrderSpawnSpec(
                FunctionalLocation: RequireString(root, "FunctionalLocation"),
                OrderNumber: RequireString(root, "OrderNumber"),
                ShortText: RequireString(root, "ShortText"),
                ServiceCategory: RequireString(root, "ServiceCategory"),
                TerritorialSetting: RequireString(root, "TerritorialSetting"),
                NotificationId: RequireGuid(root, "NotificationId"),
                ReportedAtUtc: RequireDateTimeOffset(root, "ReportedAt"));
        }
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new SapPmOrderParseException($"SAP PM work-order notification payload is missing required field '{propertyName}'.");

        return value.GetString()!;
    }

    private static Guid RequireGuid(JsonElement root, string propertyName)
    {
        var raw = RequireString(root, propertyName);
        if (!Guid.TryParse(raw, out var parsed))
            throw new SapPmOrderParseException($"SAP PM work-order notification field '{propertyName}' is not a valid GUID: '{raw}'.");

        return parsed;
    }

    private static DateTimeOffset RequireDateTimeOffset(JsonElement root, string propertyName)
    {
        var raw = RequireString(root, propertyName);
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new SapPmOrderParseException($"SAP PM work-order notification field '{propertyName}' is not a valid ISO-8601 timestamp: '{raw}'.");

        return parsed;
    }
}
