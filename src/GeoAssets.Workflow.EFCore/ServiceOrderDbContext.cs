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
/// </summary>
public class ServiceOrderDbContext(DbContextOptions<ServiceOrderDbContext> options)
    : DbContext(options)
{
    internal DbSet<ServiceOrderRecord>          ServiceOrders      => Set<ServiceOrderRecord>();
    internal DbSet<OrderDispatchRecord>         OrderDispatches    => Set<OrderDispatchRecord>();
    internal DbSet<OrderActionLogRecord>        OrderActionLogs    => Set<OrderActionLogRecord>();
    internal DbSet<OrderTypeRecord>             OrderTypes         => Set<OrderTypeRecord>();
    internal DbSet<OrderCreationPolicyRecord>   CreationPolicies   => Set<OrderCreationPolicyRecord>();
    internal DbSet<OrderActionPermissionRecord> ActionPermissions  => Set<OrderActionPermissionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceOrderDbContext).Assembly);
    }
}
