using GeoAssets.Projects.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Projects.Persistence;

/// <summary>
/// EF Core context for <see cref="ProjectRow"/> — control-plane metadata for the Project feature
/// (XD01-136), kept in its own context rather than folded into <c>GeoAssetsDbContext</c> (the
/// swappable per-org-connection feature-data/catalog store, instantiated per connection string by
/// <c>PostgresProviderFactory</c>). A Project is analogous to <c>ServiceOrder</c>/<c>Organization</c>,
/// not to <c>GeoEntityRow</c>/<c>AssetTypeRow</c> (XD01-139).
///
/// No database provider is configured here — the host supplies it via
/// <c>AddProjectPersistence</c>'s <c>configureDb</c> callback, e.g.:
/// <code>
///   services.AddProjectPersistence(o => o.UseNpgsql(connectionString));
/// </code>
/// </summary>
public class ProjectDbContext(DbContextOptions<ProjectDbContext> options)
    : DbContext(options)
{
    internal DbSet<ProjectRow> Projects => Set<ProjectRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectDbContext).Assembly);
    }
}
