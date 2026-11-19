using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.NeutralOperator.Persistence.Configurations;

/// <summary>
/// Maps <see cref="KernelTargetRecord"/> onto the kernel's <c>public."ServiceOrderTargets"</c>, excluded from this context's
/// migrations: the kernel's chain owns that table. Only the columns the vertical reads are mapped.
/// </summary>
internal sealed class KernelTargetRecordConfiguration : IEntityTypeConfiguration<KernelTargetRecord>
{
    internal const string TableName = "ServiceOrderTargets";

    /// <summary>The kernel's tables stay in PostgreSQL's default schema.</summary>
    internal const string KernelSchema = "public";

    public void Configure(EntityTypeBuilder<KernelTargetRecord> b)
    {
        b.ToTable(TableName, KernelSchema, t => t.ExcludeFromMigrations());
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasMaxLength(36);
        b.Property(t => t.ServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(t => t.FeatureId).IsRequired().HasMaxLength(64);
        b.Property(t => t.VerticalId).IsRequired().HasMaxLength(64);
        b.Property(t => t.OrganizationId).IsRequired();
    }
}
