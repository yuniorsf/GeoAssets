using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class AppliedMutationBatchRowConfiguration : IEntityTypeConfiguration<AppliedMutationBatchRow>
{
    public void Configure(EntityTypeBuilder<AppliedMutationBatchRow> e)
    {
        e.ToTable("applied_mutation_batches");
        e.HasKey(x => x.BatchId);

        e.Property(x => x.CatalogInstanceId).HasMaxLength(128).IsRequired();
        e.Property(x => x.RequestedByService).HasMaxLength(128).IsRequired();
        e.Property(x => x.AppliedByUserId).HasMaxLength(256).IsRequired();
        e.Property(x => x.SourceOrderId).HasMaxLength(36);
        e.Property(x => x.SourceOrderTypeId).HasMaxLength(128);
        e.Property(x => x.ScopeId).HasMaxLength(128);
        e.Property(x => x.AppliedItemsJson).IsRequired();
        e.Property(x => x.AppliedAtUtc).IsRequired();
        e.Property(x => x.CorrelationId).HasMaxLength(128);

        e.HasIndex(x => x.AppliedAtUtc);
    }
}
