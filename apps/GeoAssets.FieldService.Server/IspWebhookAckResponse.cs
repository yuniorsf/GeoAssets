namespace GeoAssets.FieldService.Server;

/// <summary>Shell acknowledgment for a well-formed <see cref="IspWebhookIngestRequest"/> (XD01-279).</summary>
internal sealed record IspWebhookAckResponse(string Status);
