using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core DbContext for the service order workflow domain.
///
/// No database provider is configured here — the host registers it:
/// <code>
///   // SQL Server
///   services.AddDbContext&lt;ServiceOrderDbContext&gt;(o =>
///       o.UseSqlServer(configuration.GetConnectionString("Workflow")));
///
///   // SQLite (development)
///   services.AddDbContext&lt;ServiceOrderDbContext&gt;(o =>
///       o.UseSqlite("Data Source=workflow.db"));
/// </code>
///
/// Generate migrations from the host or from this project:
/// <code>
///   dotnet ef migrations add InitialCreate --project src/GeoAssets.Workflow
/// </code>
///
/// <paramref name="tenantContext"/> is optional (defaults to <see cref="NullTenantContext"/>,
/// i.e. <see cref="Guid.Empty"/>) for the same reason as <c>GeoAssetsDbContext</c> (XD01-199):
/// every existing call site constructing this type with just
/// <see cref="DbContextOptions{TContext}"/> keeps compiling and behaving exactly as before.
/// </summary>
public class ServiceOrderDbContext(
    DbContextOptions<ServiceOrderDbContext> options,
    ITenantContext? tenantContext = null) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    internal DbSet<ServiceOrderRecord> ServiceOrders => Set<ServiceOrderRecord>();
    internal DbSet<OrderDispatchRecord> OrderDispatches => Set<OrderDispatchRecord>();
    internal DbSet<OrderActionLogRecord> OrderActionLogs => Set<OrderActionLogRecord>();
    internal DbSet<OrderTypeRecord> OrderTypes => Set<OrderTypeRecord>();
    internal DbSet<OrderCreationPolicyRecord> CreationPolicies => Set<OrderCreationPolicyRecord>();
    internal DbSet<OrderActionPermissionRecord> ActionPermissions => Set<OrderActionPermissionRecord>();
    internal DbSet<OrderTypeStateRecord> OrderTypeStates => Set<OrderTypeStateRecord>();
    internal DbSet<OrderTypeTransitionRecord> OrderTypeTransitions => Set<OrderTypeTransitionRecord>();
    internal DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();
    internal DbSet<InboxProcessedMessageRecord> InboxProcessedMessages => Set<InboxProcessedMessageRecord>();
    internal DbSet<ContractorUsLedgerEntryRecord> ContractorUsLedgerEntries => Set<ContractorUsLedgerEntryRecord>();
    internal DbSet<UsCatalogItemRecord> UsCatalogItems => Set<UsCatalogItemRecord>();
    internal DbSet<ContractorUsPostingRecord> ContractorUsPostings => Set<ContractorUsPostingRecord>();
    internal DbSet<PavingMeasurementRecord> PavingMeasurements => Set<PavingMeasurementRecord>();
    internal DbSet<ManholeAuditRecord> ManholeAudits => Set<ManholeAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceOrderDbContext).Assembly);

        // Application-layer half of tenant row isolation for the list/search surface
        // (EFServiceOrderRepository's GetAllAsync/GetByStatusAsync/etc., all funneled through
        // LoadAllAsync) — the database-layer half is the RLS policy added by the
        // AddServiceOrdersRowLevelSecurity migration, which also allows the grant-based
        // exception below via an EXISTS against OrganizationGrants.
        //
        // Deliberately NOT applied to single-record fetches used ahead of rule evaluation
        // (EFServiceOrderRepository.GetByIdAsync/UpdateAsync/AppendDispatchAsync/
        // AppendActionAsync/DeleteAsync, which explicitly call IgnoreQueryFilters()) — unlike
        // GeoEntityRow, ServiceOrderRecord already has a real cross-org delegation mechanism
        // (CrossOrgGrantRule / WorkflowOrgGrant, core/GeoAssets.Workflow/Rules) that only ever
        // runs after an unfiltered fetch. A blanket filter there would silently return null for
        // every legitimate cross-org grant target before that rule gets a chance to evaluate.
        modelBuilder.Entity<ServiceOrderRecord>()
            .HasQueryFilter(o => o.OrganizationId == _tenantContext.CurrentOrganizationId);

        // ContractorUsLedgerEntryRecord has no cross-contractor delegation mechanism (XD01-202
        // deliberately omits one — "no grant-based exception needed") so, unlike ServiceOrderRecord
        // above, this filter applies uniformly to every read path, including single-record
        // fetches: EFContractorUsLedgerRepository never calls IgnoreQueryFilters().
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
