using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ServiceOrderRecordConfiguration : IEntityTypeConfiguration<ServiceOrderRecord>
{
    public void Configure(EntityTypeBuilder<ServiceOrderRecord> b)
    {
        b.ToTable("ServiceOrders");
        b.HasKey(o => o.Id);

        b.Property(o => o.Title).IsRequired().HasMaxLength(512);
        b.Property(o => o.Description).HasMaxLength(4096);
        b.Property(o => o.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(o => o.Status).IsRequired().HasMaxLength(64);
        b.Property(o => o.CreatedBy).IsRequired().HasMaxLength(256);
        b.Property(o => o.AssignedTo).HasMaxLength(256);
        b.Property(o => o.ParentOrderId).HasMaxLength(36);
        b.Property(o => o.CreatedAt).IsRequired();
        // No HasColumnType/HasMaxLength here — an unbounded string already maps to each
        // provider's own "no length limit" text type by convention (SQL Server:
        // nvarchar(max); SQLite: TEXT; Npgsql: text). An explicit "nvarchar(max)" is
        // redundant on SQL Server and invalid syntax on SQLite (breaks EnsureCreated()),
        // which this project's test suite (GeoAssets.Workflow.EFCore.Tests) now exercises.
        b.Property(o => o.AttributesJson).IsRequired();
        b.Property(o => o.FeatureIdsJson).IsRequired();
        b.Property(o => o.SelectionSpecJson);
        b.Property(o => o.RowVersion).IsRowVersion();

        // Self-referencing hierarchy
        b.HasOne<ServiceOrderRecord>()
         .WithMany()
         .HasForeignKey(o => o.ParentOrderId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(o => o.Status);
        b.HasIndex(o => o.AssignedTo);
        b.HasIndex(o => o.CreatedBy);
        b.HasIndex(o => o.OrderTypeId);
        b.HasIndex(o => o.ParentOrderId);
        b.HasIndex(o => o.CreatedAt);

        b.HasMany(o => o.Dispatches)
         .WithOne(d => d.ServiceOrder)
         .HasForeignKey(d => d.ServiceOrderId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(o => o.ActionLog)
         .WithOne(a => a.ServiceOrder)
         .HasForeignKey(a => a.ServiceOrderId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
