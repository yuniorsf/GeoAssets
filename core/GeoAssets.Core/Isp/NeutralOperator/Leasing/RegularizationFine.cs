namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

/// <summary>
/// A back-fine owed by a tenant ISP following a clandestine-attachment regularization
/// (XD01-263). No fine-amount schedule exists anywhere in this codebase — the clandestine
/// workflow (<c>ClandestineAttachmentService</c>) only tracks flag/regularization timing, not a
/// monetary amount — so <see cref="Amount"/> is an explicit, externally-supplied value. The
/// caller (whatever process eventually owns a real fine schedule) is responsible for producing
/// these records; <see cref="LeaseBillingEngine"/> only folds them into the right tenant's
/// invoice as a separate line item.
/// </summary>
/// <param name="TenantCnpj">The operator CNPJ the fine is billed against.</param>
/// <param name="Amount">The fine amount, in the same currency as <see cref="LeaseTariffSchedule.TariffPerFixationPoint"/>.</param>
/// <param name="Reference">A human-readable reference (e.g. the clandestine-notice service order id) for audit traceability.</param>
public sealed record RegularizationFine(string TenantCnpj, decimal Amount, string Reference);
