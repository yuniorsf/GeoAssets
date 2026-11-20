using System.Data.Common;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Persistence.Configurations;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// The NeutralOperator vertical's own EF Core context (XD01-380, ADR-001 Phase 6): pole occupancies, their entries, the
/// occupancy change sets staged on service-order targets and the approvals' apply sagas (XD01-383), in schema <see cref="Schema"/> with their own migrations
/// (this assembly) and history table (<see cref="MigrationsHistoryTableName"/>) — moved out of the kernel's
/// <c>ServiceOrderDbContext</c>, whose chains no longer map them.
///
/// <para><b>Until Phase 7 it shares the kernel's connection and transaction.</b> The host builds it over the scope's
/// kernel connection (<see cref="NeutralOperatorPersistenceExtensions.AddNeutralOperatorPersistence(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>),
/// and every store calls <see cref="JoinAmbientTransaction"/> first, so an occupancy write made while the kernel's unit of
/// work, order repository or outbox has a transaction open is part of that transaction — the approval's order transition,
/// occupancy commit and outbox message stay one atomic write.</para>
///
/// <para><b>Tenancy</b> is the kernel's: the same uniform owner-only query filters the kernel context applied to these
/// tables, and on PostgreSQL the tables' row-level-security policies, which moved with them. <paramref name="tenantContext"/>
/// is optional for the same reason as the kernel's (defaults to <see cref="NullTenantContext"/>).</para>
/// </summary>
public class NeutralOperatorDbContext(
    DbContextOptions<NeutralOperatorDbContext> options,
    ITenantContext? tenantContext = null,
    IAmbientDbTransaction? ambientTransaction = null) : DbContext(options)
{
    /// <summary>The PostgreSQL schema of the vertical's tables and of its migrations history.</summary>
    public const string Schema = "neutral_operator";

    /// <summary>The history table of this context's migration chain, in <see cref="Schema"/>.</summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory_neutral_operator";

    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    /// <summary>The ambient transaction this context joined last, so a stale one is let go and its own is never.</summary>
    private DbTransaction? _joined;

    internal DbSet<PoleOccupancyRecord> PoleOccupancies => Set<PoleOccupancyRecord>();
    internal DbSet<PoleOccupancyEntryRecord> PoleOccupancyEntries => Set<PoleOccupancyEntryRecord>();
    internal DbSet<OccupancyChangeSetRecord> OccupancyChangeSets => Set<OccupancyChangeSetRecord>();
    internal DbSet<OccupancyApplySagaRecord> OccupancyApplySagas => Set<OccupancyApplySagaRecord>();
    internal DbSet<OccupancyApplySagaPoleRecord> OccupancyApplySagaPoles => Set<OccupancyApplySagaPoleRecord>();

    /// <summary>The kernel's targets, read-only (see <see cref="KernelTargetRecord"/>).</summary>
    internal DbSet<KernelTargetRecord> ServiceOrderTargets => Set<KernelTargetRecord>();

    private const string KernelVerticalId = "kernel";

    /// <summary>The organization this context is scoped to — the one its tenant query filters compare against.</summary>
    internal Guid CurrentOrganizationId => _tenantContext.CurrentOrganizationId;

    /// <summary>
    /// The Npgsql options every registration of this context uses — the runtime one and the design-time factory alike:
    /// migrations from this assembly, history in <see cref="Schema"/>.<see cref="MigrationsHistoryTableName"/>.
    /// </summary>
    public static void ConfigureMigrations(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);

        npgsql.MigrationsAssembly(typeof(NeutralOperatorDbContext).Assembly.GetName().Name)
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
                "The NeutralOperator context has a transaction of its own while the shared connection has another one open.");
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NeutralOperatorDbContext).Assembly);

        ConfigurePoleOccupancy(modelBuilder);
        ConfigureOccupancyChangeSets(modelBuilder);
        ConfigureOccupancyApplySagas(modelBuilder);

        // The kernel applies the same filter to its targets; this read is the one staging makes, and staging is the pole
        // owner's (the change set's row-level-security WITH CHECK pins the target to that same organization). Only the
        // kernel's in-process orders stage occupancy: a remote vertical's ledger row (XD01-379) never counts as a target.
        modelBuilder.Entity<KernelTargetRecord>()
            .HasQueryFilter(t => t.OrganizationId == _tenantContext.CurrentOrganizationId && t.VerticalId == KernelVerticalId);
    }

    private void ConfigureOccupancyChangeSets(ModelBuilder modelBuilder)
    {
        // OccupancyChangeSetRecord (XD01-315) — uniform-filter treatment, like the occupancy it proposes to change: a
        // staged set names the occupants of the pole owner's poles, so it does not follow its order to a cross-organization
        // grantee the way a target does. EFOccupancyChangeSetRepository calls IgnoreQueryFilters() only to discard what is
        // still staged when the order closes, whoever closes it. The database-layer half is the table's row-level-security
        // policy (AddServiceOrderOccupancyChangeSetsRowLevelSecurity, adopted by this context's baseline).
        modelBuilder.Entity<OccupancyChangeSetRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        if (Database.IsNpgsql())
            OccupancyChangeSetRecordConfiguration.UseOrdinalTextOrder(modelBuilder.Entity<OccupancyChangeSetRecord>());
    }

    private void ConfigureOccupancyApplySagas(ModelBuilder modelBuilder)
    {
        // The approval sagas (XD01-383) — owner only on every request, like the occupancy they hold open. The timeout sweeper
        // alone reads the headers across organizations (IgnoreQueryFilters): they hold ids, states and instants, no
        // occupancy entry, and no row-level-security policy, like the outbox. The poles' snapshots hold entries: the uniform
        // filter here, and their table's row-level-security policy (AddOccupancyApplySagas) at the database.
        modelBuilder.Entity<OccupancyApplySagaRecord>()
            .HasQueryFilter(s => s.OrganizationId == _tenantContext.CurrentOrganizationId);
        modelBuilder.Entity<OccupancyApplySagaPoleRecord>()
            .HasQueryFilter(p => p.OrganizationId == _tenantContext.CurrentOrganizationId);

        if (Database.IsNpgsql())
            OccupancyApplySagaPoleRecordConfiguration.UseOrdinalTextOrder(modelBuilder.Entity<OccupancyApplySagaPoleRecord>());
    }

    private void ConfigurePoleOccupancy(ModelBuilder modelBuilder)
    {
        // PoleOccupancyRecord and PoleOccupancyEntryRecord (XD01-314) — uniform-filter treatment: occupancy belongs to the
        // pole owner and has no cross-organization delegation (an occupant never reads it, not even the entries that name
        // it), so EFPoleOccupancyRepository never calls IgnoreQueryFilters(). The database-layer half is the tables'
        // row-level-security policies (AddPoleOccupanciesRowLevelSecurity, adopted by this context's baseline).
        modelBuilder.Entity<PoleOccupancyRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);
        modelBuilder.Entity<PoleOccupancyEntryRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        // PostgreSQL only: see PoleOccupancyRecordConfiguration.UseOrdinalTextOrder.
        if (Database.IsNpgsql())
            PoleOccupancyRecordConfiguration.UseOrdinalTextOrder(modelBuilder.Entity<PoleOccupancyRecord>());
    }
}
