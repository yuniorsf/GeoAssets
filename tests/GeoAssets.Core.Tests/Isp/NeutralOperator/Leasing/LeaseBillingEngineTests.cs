using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Leasing;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Leasing;

public class LeaseBillingEngineTests
{
    private static readonly LeaseTariffSchedule Tariff = new(TariffPerFixationPoint: 42.50m);

    private static PoleAttachmentSlotMatrix PoleWith(string poleFeatureId, params PoleAttachmentSlot[] slots)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = poleFeatureId };
        foreach (var slot in slots)
            matrix.AddSlot(slot);
        return matrix;
    }

    private static PoleAttachmentSlot Slot(int index, PoleAttachmentOccupantStatus status, string? operatorCnpj = null) =>
        new() { SlotIndex = index, Status = status, OperatorCnpj = operatorCnpj };

    [Fact]
    public void GenerateInvoices_OnlyAuthorizedContractedSlotsCount_OneInvoicePerTenant()
    {
        var engine = new LeaseBillingEngine();
        var poles = new[]
        {
            PoleWith(
                "pole-1",
                Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted, "11.111.111/0001-11"),
                Slot(2, PoleAttachmentOccupantStatus.AuthorizedContracted, "22.222.222/0001-22"),
                Slot(3, PoleAttachmentOccupantStatus.ClandestineUnauthorized, "33.333.333/0001-33"),
                Slot(4, PoleAttachmentOccupantStatus.AbandonedDeadDrop, "44.444.444/0001-44"),
                Slot(5, PoleAttachmentOccupantStatus.ActiveReservation)),
            PoleWith(
                "pole-2",
                Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted, "11.111.111/0001-11")),
        };

        var invoices = engine.GenerateInvoices(poles, Tariff, [], new DateOnly(2026, 3, 1));

        invoices.Should().HaveCount(2, "only tenants 11... and 22... have an AuthorizedContracted slot");
        var tenant11 = invoices.Single(i => string.Equals(i.TenantCnpj, "11.111.111/0001-11", StringComparison.Ordinal));
        tenant11.LineItems.Should().ContainSingle(l => l.Kind == LeaseInvoiceLineItemKind.FixationCharge);
        tenant11.Total.Should().Be(2 * 42.50m, "tenant 11... holds 2 fixation points across both poles");

        var tenant22 = invoices.Single(i => string.Equals(i.TenantCnpj, "22.222.222/0001-22", StringComparison.Ordinal));
        tenant22.Total.Should().Be(42.50m);

        invoices.Should().NotContain(
            i => string.Equals(i.TenantCnpj, "33.333.333/0001-33", StringComparison.Ordinal)
                || string.Equals(i.TenantCnpj, "44.444.444/0001-44", StringComparison.Ordinal),
            "clandestine and abandoned-dead-drop slots are not verified/active attachments");
    }

    [Fact]
    public void GenerateInvoices_RegularizationFines_AppearAsSeparateLineItems()
    {
        var engine = new LeaseBillingEngine();
        var poles = new[]
        {
            PoleWith("pole-1", Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted, "11.111.111/0001-11")),
        };
        var fines = new[]
        {
            new RegularizationFine("11.111.111/0001-11", 500m, "clandestine-notice-abc"),
        };

        var invoices = engine.GenerateInvoices(poles, Tariff, fines, new DateOnly(2026, 3, 1));

        var invoice = invoices.Single(i => string.Equals(i.TenantCnpj, "11.111.111/0001-11", StringComparison.Ordinal));
        invoice.LineItems.Should().HaveCount(2);
        invoice.LineItems.Should().ContainSingle(l => l.Kind == LeaseInvoiceLineItemKind.FixationCharge && l.Amount == 42.50m);
        invoice.LineItems.Should().ContainSingle(l => l.Kind == LeaseInvoiceLineItemKind.RegularizationFine && l.Amount == 500m);
        invoice.Total.Should().Be(542.50m);
    }

    [Fact]
    public void GenerateInvoices_FineWithNoActiveAttachment_StillProducesAnInvoice()
    {
        var engine = new LeaseBillingEngine();
        var fines = new[] { new RegularizationFine("99.999.999/0001-99", 100m, "ref-1") };

        var invoices = engine.GenerateInvoices([], Tariff, fines, new DateOnly(2026, 3, 1));

        invoices.Should().ContainSingle(i => string.Equals(i.TenantCnpj, "99.999.999/0001-99", StringComparison.Ordinal) && i.Total == 100m,
            "a fine is owed even when the tenant currently holds no fixation points");
    }

    [Fact]
    public void GenerateInvoices_CalledTwiceWithIdenticalInputs_ProducesValueIdenticalResults()
    {
        // The "dry run reproduces the prior month's totals exactly when no attachment changes
        // occurred" acceptance criterion, proven directly: this engine is a pure function of
        // its inputs, so two calls with unchanged poles/tariff/fines must agree exactly.
        var engine = new LeaseBillingEngine();
        var poles = new[]
        {
            PoleWith(
                "pole-1",
                Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted, "11.111.111/0001-11"),
                Slot(2, PoleAttachmentOccupantStatus.AuthorizedContracted, "22.222.222/0001-22")),
        };
        var fines = new[] { new RegularizationFine("22.222.222/0001-22", 75m, "ref-xyz") };

        var dryRun = engine.GenerateInvoices(poles, Tariff, fines, new DateOnly(2026, 3, 1), dryRun: true);
        var realRun = engine.GenerateInvoices(poles, Tariff, fines, new DateOnly(2026, 3, 1), dryRun: false);

        dryRun.Should().BeEquivalentTo(realRun, "a dry run and a real run over unchanged inputs must agree exactly");
    }

    [Fact]
    public void GenerateInvoices_EmptyPolesAndFines_ProducesNoInvoices()
    {
        var engine = new LeaseBillingEngine();

        var invoices = engine.GenerateInvoices([], Tariff, [], new DateOnly(2026, 3, 1));

        invoices.Should().BeEmpty();
    }
}
