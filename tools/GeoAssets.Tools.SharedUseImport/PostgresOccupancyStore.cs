using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Persistence.Postgres.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Opens the real store: <see cref="EFPoleOccupancyRepository"/> on a
/// <see cref="NeutralOperatorDbContext"/> (XD01-380) of its own, scoped to the pole owner, with the
/// same session interceptor the hosts use so row-level security sees that organization too.
/// </summary>
internal static class PostgresOccupancyStore
{
    public static OccupancyStoreSession Open(SharedUseImportOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var tenant = new FixedTenantContext(options.OrganizationId);
        var dbOptions = new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(options.ConnectionString, NeutralOperatorDbContext.ConfigureMigrations)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options;
        NeutralOperatorDbContext? db = null;
        try
        {
            db = new NeutralOperatorDbContext(dbOptions, tenant);
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
