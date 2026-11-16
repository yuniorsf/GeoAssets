namespace GeoAssets.FieldService.Server;

/// <summary>Inbound settlement-webhook body — see <c>SettlementWebhookPayload</c>.</summary>
internal sealed record LeaseInvoiceSettlementWebhookRequest(string TenantCnpj, DateOnly BillingPeriod, DateTimeOffset PaidAtUtc, string ExternalReference);
