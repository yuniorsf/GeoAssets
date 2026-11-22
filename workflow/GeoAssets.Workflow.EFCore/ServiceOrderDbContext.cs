using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Persistence.Configurations;
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
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    internal DbSet<ServiceOrderRecord> ServiceOrders => Set<ServiceOrderRecord>();
    internal DbSet<OrderDispatchRecord> OrderDispatches => Set<OrderDispatchRecord>();
    internal DbSet<OrderActionLogRecord> OrderActionLogs => Set<OrderActionLogRecord>();
    internal DbSet<OrderTypeRecord> OrderTypes => Set<OrderTypeRecord>();
    internal DbSet<OrderCreationPolicyRecord> CreationPolicies => Set<OrderCreationPolicyRecord>();
    internal DbSet<OrderActionPermissionRecord> ActionPermissions => Set<OrderActionPermissionRecord>();
    internal DbSet<OrderTypeStateRecord> OrderTypeStates => Set<OrderTypeStateRecord>();
    internal DbSet<OrderTypeTransitionRecord> OrderTypeTransitions => Set<OrderTypeTransitionRecord>();
    internal DbSet<OrderTypeRecipientActionRecord> OrderTypeRecipientActions => Set<OrderTypeRecipientActionRecord>();
    internal DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();
    internal DbSet<InboxProcessedMessageRecord> InboxProcessedMessages => Set<InboxProcessedMessageRecord>();
    internal DbSet<OrganizationGrantMirrorRecord> OrganizationGrantMirror => Set<OrganizationGrantMirrorRecord>();
    internal DbSet<OrganizationGrantSyncCursorRecord> OrganizationGrantSyncCursor => Set<OrganizationGrantSyncCursorRecord>();
    internal DbSet<ScopedRoleAssignmentMirrorRecord> ScopedRoleAssignmentMirror => Set<ScopedRoleAssignmentMirrorRecord>();
    internal DbSet<ScopedRoleAssignmentSyncCursorRecord> ScopedRoleAssignmentSyncCursor => Set<ScopedRoleAssignmentSyncCursorRecord>();
    internal DbSet<ServiceOrderTargetRecord> ServiceOrderTargets => Set<ServiceOrderTargetRecord>();

    /// <summary>The organization this context is scoped to — the one its tenant query filters compare against.</summary>
    internal Guid CurrentOrganizationId => _tenantContext.CurrentOrganizationId;

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

        ConfigureServiceOrderTargets(modelBuilder);
    }

    private void ConfigureServiceOrderTargets(ModelBuilder modelBuilder)
    {
        // ServiceOrderTargetRecord (XD01-306) follows its parent ServiceOrderRecord, not the
        // uniform-filter treatment of the verticals' tables: the filter applies to the one cross-order search
        // (EFServiceOrderTargetRepository.FindOpenUsagesAsync, when it has no order to scope by),
        // while every operation addressed to a single order calls IgnoreQueryFilters() — a
        // cross-org grantee authorized on the order by CrossOrgGrantRule must reach its targets
        // too. The database-layer half is the AddServiceOrderTargetsRowLevelSecurity migration.
        modelBuilder.Entity<ServiceOrderTargetRecord>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);

        // This project references no database provider, so the one provider-specific piece of
        // the model is keyed on the provider's name: see UseOrdinalTextOrder for why PostgreSQL
        // (and only PostgreSQL) needs it.
        if (string.Equals(Database.ProviderName, PostgreSqlProviderName, StringComparison.Ordinal))
            ServiceOrderTargetRecordConfiguration.UseOrdinalTextOrder(modelBuilder.Entity<ServiceOrderTargetRecord>());
    }
}
