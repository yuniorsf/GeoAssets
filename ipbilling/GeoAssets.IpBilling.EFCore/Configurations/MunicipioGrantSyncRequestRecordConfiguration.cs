using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class MunicipioGrantSyncRequestRecordConfiguration : IEntityTypeConfiguration<MunicipioGrantSyncRequestRecord>
{
    /// <summary>The longest <see cref="MunicipioGrantSyncRequestRecord.LastError"/> stored.</summary>
    public const int MaxErrorLength = 1024;

    /// <summary>The longest <see cref="MunicipioGrantSyncRequestRecord.RequestedBy"/> stored.</summary>
    public const int MaxRequestedByLength = 256;

    /// <summary>The longest <see cref="MunicipioGrantSyncRequestRecord.LeasedBy"/> stored.</summary>
    public const int MaxWorkerIdLength = 128;

    public void Configure(EntityTypeBuilder<MunicipioGrantSyncRequestRecord> b)
    {
        b.ToTable("MunicipioGrantSyncRequests");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedOnAdd();

        b.Property(r => r.MunicipioId).IsRequired();
        b.Property(r => r.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.IbgeCode).IsRequired().HasMaxLength(7);
        b.Property(r => r.DistribuidoraOrganizationId).IsRequired();
        b.Property(r => r.PreviousIbgeCode).HasMaxLength(7);
        b.Property(r => r.RequestedBy).IsRequired().HasMaxLength(MaxRequestedByLength);
        b.Property(r => r.RequestedAt).IsRequired();
        b.Property(r => r.Attempts).IsRequired();
        b.Property(r => r.NextAttemptAt).IsRequired();
        b.Property(r => r.LeasedBy).HasMaxLength(MaxWorkerIdLength);
        b.Property(r => r.LastError).HasMaxLength(MaxErrorLength);

        // The head-of-line check (the oldest unfinished request of a município) and the município status read.
        b.HasIndex(r => new { r.MunicipioId, r.Id });

        // The dispatcher's due scan, over unfinished requests only.
        b.HasIndex(r => r.NextAttemptAt)
         .HasFilter("\"CompletedAt\" IS NULL AND \"FailedAt\" IS NULL");
    }
}
