using System.Data.Common;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.FieldService.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GeoAssets.FieldService.Persistence;

/// <summary>
/// The FieldService vertical's own EF Core context (XD01-381, ADR-001 Phase 6): the contractor US ledger, the US catalog,
/// the contractor US postings, the ICERP paving measurements and the manhole audits, in schema <see cref="Schema"/> with
/// their own migrations (this assembly) and history table (<see cref="MigrationsHistoryTableName"/>) — moved out of the
/// kernel's <c>ServiceOrderDbContext</c>, whose chains no longer map them.
///
/// <para><b>Until Phase 7 it shares the kernel's connection and transaction.</b> The host builds it over the scope's
/// kernel connection (<see cref="FieldServicePersistenceExtensions.AddFieldServicePersistence(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>),
/// and every store calls <see cref="JoinAmbientTransaction"/> first, so a write made while the kernel's unit of work, order
/// repository or outbox has a transaction open is part of that transaction — the inspection commit's postings, order
/// transition, paving child order, paving measurement and outbox message stay one atomic write
/// (<see cref="InspectionCommitService"/>).</para>
///
/// <para><b>Tenancy</b> is the kernel's: the same uniform owner-only query filters the kernel context applied to these
/// tables (none of them has a cross-organization delegation, so no store calls <c>IgnoreQueryFilters()</c>), and on
/// PostgreSQL the tables' row-level-security policies, which moved with them. <paramref name="tenantContext"/> is optional
/// for the same reason as the kernel's (defaults to <see cref="NullTenantContext"/>).</para>
///
/// <para>The foreign keys of the postings and paving measurements to the kernel's <c>public."ServiceOrders"</c> are
/// database-only: this model does not map the kernel's orders.</para>
/// </summary>
public class FieldServiceDbContext(
    DbContextOptions<FieldServiceDbContext> options,
    ITenantContext? tenantContext = null,
    IAmbientDbTransaction? ambientTransaction = null) : DbContext(options)
{
    /// <summary>The PostgreSQL schema of the vertical's tables and of its migrations history.</summary>
    public const string Schema = "field_service";

    /// <summary>The history table of this context's migration chain, in <see cref="Schema"/>.</summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory_field_service";

    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    /// <summary>The ambient transaction this context joined last, so a stale one is let go and its own is never.</summary>
    private DbTransaction? _joined;

    internal DbSet<ContractorUsLedgerEntryRecord> ContractorUsLedgerEntries => Set<ContractorUsLedgerEntryRecord>();
    internal DbSet<UsCatalogItemRecord> UsCatalogItems => Set<UsCatalogItemRecord>();
    internal DbSet<ContractorUsPostingRecord> ContractorUsPostings => Set<ContractorUsPostingRecord>();
    internal DbSet<PavingMeasurementRecord> PavingMeasurements => Set<PavingMeasurementRecord>();
    internal DbSet<ManholeAuditRecord> ManholeAudits => Set<ManholeAuditRecord>();

    /// <summary>The organization this context is scoped to — the one its tenant query filters compare against.</summary>
    internal Guid CurrentOrganizationId => _tenantContext.CurrentOrganizationId;

    /// <summary>
    /// The Npgsql options every registration of this context uses — the runtime one and the design-time factory alike:
    /// migrations from this assembly, history in <see cref="Schema"/>.<see cref="MigrationsHistoryTableName"/>.
    /// </summary>
    public static void ConfigureMigrations(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);

        npgsql.MigrationsAssembly(typeof(FieldServiceDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(MigrationsHistoryTableName, Schema);
    }

    /// <summary>
    /// Makes this context use the ambient transaction (<see cref="IAmbientDbTransaction.Current"/>) when there is one, and
    /// forgets one that has ended. A transaction this context began itself is left alone. Called by every store before it
    /// touches the database; cheap when nothing changed.
    /// </summary>
    internal void JoinAmbientTransaction()
    {
        var ambient = ambientTransaction?.Current;
        var inUse = Database.CurrentTransaction?.GetDbTransaction();
        if (ReferenceEquals(ambient, inUse))
            return;

        var inUseIsJoined = inUse is not null && ReferenceEquals(inUse, _joined);
        if (inUse is not null && !inUseIsJoined)
        {
            if (ambient is null)
                return;

            throw new InvalidOperationException(
                "The FieldService context has a transaction of its own while the shared connection has another one open.");
        }

        if (inUseIsJoined)
            Database.UseTransaction(null);

        _joined = ambient;
        if (ambient is not null)
            Database.UseTransaction(ambient);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FieldServiceDbContext).Assembly);

        // ContractorUsLedgerEntryRecord has no cross-contractor delegation mechanism (XD01-202
        // deliberately omits one — "no grant-based exception needed") so, unlike the kernel's
        // ServiceOrderRecord, this filter applies uniformly to every read path, including
        // single-record fetches: EFContractorUsLedgerRepository never calls IgnoreQueryFilters().
        modelBuilder.Entity<ContractorUsLedgerEntryRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        // UsCatalogItemRecord (XD01-241) has no cross-contractor delegation mechanism either —
        // same reasoning and same uniform-filter treatment as ContractorUsLedgerEntryRecord
        // above: EFUsCatalogRepository never calls IgnoreQueryFilters().
        modelBuilder.Entity<UsCatalogItemRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        // ContractorUsPostingRecord (XD01-242) — same uniform-filter treatment, scoped by
        // ContractorOrganizationId: EFContractorUsPostingRepository never calls
        // IgnoreQueryFilters() either.
        modelBuilder.Entity<ContractorUsPostingRecord>()
            .HasQueryFilter(e => e.ContractorOrganizationId == _tenantContext.CurrentOrganizationId);

        // PavingMeasurementRecord (XD01-291) — same uniform-filter treatment as
        // ContractorUsPostingRecord: EFPavingMeasurementRepository never calls
        // IgnoreQueryFilters() either.
        modelBuilder.Entity<PavingMeasurementRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        // ManholeAuditRecord (XD01-265) — same uniform-filter treatment: EFManholeAuditRepository
        // never calls IgnoreQueryFilters() either.
        modelBuilder.Entity<ManholeAuditRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);
    }
}
