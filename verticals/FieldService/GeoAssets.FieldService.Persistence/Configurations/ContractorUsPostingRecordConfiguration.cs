using GeoAssets.FieldService.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.FieldService.Persistence.Configurations;

internal sealed class ContractorUsPostingRecordConfiguration : IEntityTypeConfiguration<ContractorUsPostingRecord>
{
    public void Configure(EntityTypeBuilder<ContractorUsPostingRecord> b)
    {
        // Snake_case, matching this ticket's own literal table name — unlike this assembly's
        // other PascalCase tables (ContractorUsLedgerEntries, UsCatalogItems).
        b.ToTable("field_contractor_us_postings");
        b.HasKey(e => e.Id);

        b.Property(e => e.ServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(e => e.ContractorOrganizationId).IsRequired();
        b.Property(e => e.ItemCode).IsRequired().HasMaxLength(64);
        b.Property(e => e.Quantity).IsRequired().HasPrecision(18, 4);
        b.Property(e => e.UnitUs).IsRequired().HasPrecision(18, 6);
        b.Property(e => e.TotalUs).IsRequired().HasPrecision(18, 6);
        b.Property(e => e.MonetaryRate).IsRequired().HasPrecision(18, 6);
        b.Property(e => e.TotalBrl).IsRequired().HasPrecision(18, 2);
        b.Property(e => e.PostedAtUtc).IsRequired();

        // Real FK to the kernel's ServiceOrders, per XD01-242's own AC — deliberately Restrict, not
        // Cascade: deleting a ServiceOrder must never silently destroy its posted ledger history.
        // Database-only since XD01-381: this context does not map the kernel's orders, so the
        // constraint (FK_field_contractor_us_postings_ServiceOrders_ServiceOrderId → public."ServiceOrders")
        // is the one the kernel chain created and the AdoptFieldServiceTables baseline keeps (or
        // creates, on a database the kernel chain never created this table in).

        // ContractorOrganizationId is deliberately NOT a literal FK to Identity's own
        // Organizations table (a different DbContext/assembly, apps/GeoAssets.Identity.EFCore) —
        // GeoAssets.Workflow never references GeoAssets.Identity directly (see
        // GeoAssets.Workflow.Rules.WorkflowOrgGrant's own doc comment for the same reasoning),
        // and every other org-scoped table in this codebase (ContractorUsLedgerEntries,
        // UsCatalogItems, ServiceOrders itself) treats OrganizationId as a plain scalar too, not
        // a DB-enforced FK. Indexed below to satisfy the "per-contractor" half of this ticket's
        // indexing AC.
        b.HasIndex(e => new { e.ContractorOrganizationId, e.PostedAtUtc });
        b.HasIndex(e => e.ServiceOrderId);
    }
}
