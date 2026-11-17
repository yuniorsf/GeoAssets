using GeoAssets.Provider.PostgreSQL.Interceptors;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Opens the real store: <see cref="EFPoleOccupancyRepository"/> on a
/// <see cref="ServiceOrderDbContext"/> scoped to the pole owner, with the same session
/// interceptor the hosts use so row-level security sees that organization too.
/// </summary>
internal static class PostgresOccupancyStore
{
    public static OccupancyStoreSession Open(SharedUseImportOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var tenant = new FixedTenantContext(options.OrganizationId);
        var dbOptions = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(options.ConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options;
        ServiceOrderDbContext? db = null;
        try
        {
            db = new ServiceOrderDbContext(dbOptions, tenant);
            var repository = new EFPoleOccupancyRepository(db, timeProvider);
            var session = new OccupancyStoreSession(repository, repository, db);
            db = null;
            return session;
        }
        finally
        {
            db?.Dispose();
        }
    }
}
