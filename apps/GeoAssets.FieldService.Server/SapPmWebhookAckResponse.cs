namespace GeoAssets.FieldService.Server;

/// <summary>Acknowledgment for a processed <see cref="SapPmWebhookIngestRequest"/> (XD01-231).</summary>
internal sealed record SapPmWebhookAckResponse(string Status, string? ServiceOrderId);
