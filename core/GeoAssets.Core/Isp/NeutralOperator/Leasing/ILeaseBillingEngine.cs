namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

/// <summary>
/// Monthly Poste Justo lease-billing engine for a Neutral Infrastructure Operator (XD01-267).
/// Computes <c>Σ(FixationPoints × Tariff) + Fines</c> per tenant ISP. A pure function of its
/// inputs — calling it twice with the same <paramref name="poles"/>/<paramref name="tariff"/>/
/// <paramref name="fines"/> always produces value-identical invoices, which is what makes a
/// dry run meaningful: a dry run and a real run compute the exact same totals, since this
/// engine has no side effects of its own. Actually posting the result to a ledger/ERP is a
/// separate, not-yet-built caller's responsibility.
/// </summary>
public interface ILeaseBillingEngine
{
    /// <summary>
    /// Produces one invoice per tenant ISP (keyed by <see cref="PoleAttachmentSlot.OperatorCnpj"/>)
    /// that currently has at least one <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/>
    /// slot, or an outstanding <paramref name="fines"/> entry. Every other occupant status
    /// (<see cref="PoleAttachmentOccupantStatus.ActiveReservation"/>,
    /// <see cref="PoleAttachmentOccupantStatus.AbandonedDeadDrop"/>,
    /// <see cref="PoleAttachmentOccupantStatus.ClandestineUnauthorized"/>) is excluded from the
    /// fixation-point count — this is the "verified/regularized attachments only" rule: a slot
    /// that was flagged clandestine and later regularized is only billable once its own
    /// <see cref="PoleAttachmentSlot.Status"/> has actually been moved back to
    /// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> by the caller — this
    /// engine does not read the clandestine-notice workflow's order state itself.
    /// </summary>
    /// <param name="poles">Every pole's current attachment-slot matrix.</param>
    /// <param name="tariff">The configured per-fixation-point monthly rate.</param>
    /// <param name="fines">Any back-fines to bill separately this period.</param>
    /// <param name="billingPeriod">The month this run bills for (informational — does not affect the totals).</param>
    /// <param name="dryRun">
    /// When <c>true</c>, documents caller intent not to post these invoices anywhere; it does
    /// not change the computed totals, since this engine never has side effects regardless.
    /// </param>
    IReadOnlyList<LeaseInvoice> GenerateInvoices(
        IReadOnlyCollection<PoleAttachmentSlotMatrix> poles,
        LeaseTariffSchedule tariff,
        IReadOnlyCollection<RegularizationFine> fines,
        DateOnly billingPeriod,
        bool dryRun = false);
}
