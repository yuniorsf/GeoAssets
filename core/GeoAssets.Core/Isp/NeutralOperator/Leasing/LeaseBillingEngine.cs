namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

/// <summary>Default implementation of <see cref="ILeaseBillingEngine"/>.</summary>
public sealed class LeaseBillingEngine : ILeaseBillingEngine
{
    public IReadOnlyList<LeaseInvoice> GenerateInvoices(
        IReadOnlyCollection<PoleAttachmentSlotMatrix> poles,
        LeaseTariffSchedule tariff,
        IReadOnlyCollection<RegularizationFine> fines,
        DateOnly billingPeriod,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(poles);
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(fines);
        _ = dryRun; // Documented as a no-op: this engine never has side effects to suppress.

        var fixationPointsByTenant = CountFixationPointsByTenant(poles);
        var finesByTenant = GroupFinesByTenant(fines);

        var tenants = new SortedSet<string>(StringComparer.Ordinal);
        tenants.UnionWith(fixationPointsByTenant.Keys);
        tenants.UnionWith(finesByTenant.Keys);

        return tenants
            .Select(tenantCnpj => BuildInvoice(tenantCnpj, billingPeriod, tariff, fixationPointsByTenant, finesByTenant))
            .ToList();
    }

    private static Dictionary<string, int> CountFixationPointsByTenant(IEnumerable<PoleAttachmentSlotMatrix> poles)
    {
        var fixationPointsByTenant = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pole in poles)
        {
            foreach (var slot in pole.Slots)
            {
                if (slot.Status != PoleAttachmentOccupantStatus.AuthorizedContracted || slot.OperatorCnpj is null)
                    continue;

                fixationPointsByTenant[slot.OperatorCnpj] =
                    fixationPointsByTenant.GetValueOrDefault(slot.OperatorCnpj) + 1;
            }
        }

        return fixationPointsByTenant;
    }

    private static Dictionary<string, List<RegularizationFine>> GroupFinesByTenant(IEnumerable<RegularizationFine> fines)
    {
        var finesByTenant = new Dictionary<string, List<RegularizationFine>>(StringComparer.Ordinal);
        foreach (var fine in fines)
        {
            if (!finesByTenant.TryGetValue(fine.TenantCnpj, out var tenantFines))
            {
                tenantFines = [];
                finesByTenant[fine.TenantCnpj] = tenantFines;
            }

            tenantFines.Add(fine);
        }

        return finesByTenant;
    }

    private static LeaseInvoice BuildInvoice(
        string tenantCnpj,
        DateOnly billingPeriod,
        LeaseTariffSchedule tariff,
        Dictionary<string, int> fixationPointsByTenant,
        Dictionary<string, List<RegularizationFine>> finesByTenant)
    {
        var lineItems = new List<LeaseInvoiceLineItem>();

        if (fixationPointsByTenant.TryGetValue(tenantCnpj, out var fixationPoints) && fixationPoints > 0)
        {
            var charge = Math.Round(fixationPoints * tariff.TariffPerFixationPoint, 2, MidpointRounding.AwayFromZero);
            lineItems.Add(new LeaseInvoiceLineItem(
                $"Poste Justo lease — {fixationPoints} fixation point(s) at {tariff.TariffPerFixationPoint:F2}/point",
                charge,
                LeaseInvoiceLineItemKind.FixationCharge));
        }

        if (finesByTenant.TryGetValue(tenantCnpj, out var tenantFines))
        {
            foreach (var fine in tenantFines)
            {
                lineItems.Add(new LeaseInvoiceLineItem(
                    $"Regularization back-fine — {fine.Reference}",
                    Math.Round(fine.Amount, 2, MidpointRounding.AwayFromZero),
                    LeaseInvoiceLineItemKind.RegularizationFine));
            }
        }

        return new LeaseInvoice(tenantCnpj, billingPeriod, lineItems);
    }
}
