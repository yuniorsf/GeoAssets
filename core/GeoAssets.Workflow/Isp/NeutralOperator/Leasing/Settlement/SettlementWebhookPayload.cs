namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>Inbound reconciliation notice that one tenant's billing-period invoice was paid.</summary>
public sealed record SettlementWebhookPayload(string TenantCnpj, DateOnly BillingPeriod, DateTimeOffset PaidAtUtc, string ExternalReference);
