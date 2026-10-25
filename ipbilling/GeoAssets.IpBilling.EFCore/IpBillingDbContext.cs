using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>
/// EF Core DbContext for the IP-billing (GEOS_FAT_IP) domain's reference/control-plane data —
/// <see cref="LampTypes"/>/<see cref="LampWattageOptions"/>, <see cref="Municipios"/>,
/// <see cref="UnidadesOrganizacionais"/>, <see cref="IpTransformerIndexes"/>, and the
/// <see cref="Faturamentos"/> ledger (schema-only in this ticket, XD01-176 — see
/// <c>Models.Faturamento</c>'s own doc comment).
///
/// No database provider is configured here — the host registers it, same convention as
/// <c>ServiceOrderDbContext</c>/<c>ProjectDbContext</c>:
/// <code>
///   services.AddIpBillingPersistence(o => o.UseNpgsql(connectionString,
///       npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
/// </code>
/// </summary>
public class IpBillingDbContext(DbContextOptions<IpBillingDbContext> options)
    : DbContext(options)
{
    internal DbSet<LampTypeRecord>              LampTypes              => Set<LampTypeRecord>();
    internal DbSet<LampWattageOptionRecord>      LampWattageOptions     => Set<LampWattageOptionRecord>();
    internal DbSet<MunicipioRecord>             Municipios             => Set<MunicipioRecord>();
    internal DbSet<UnidadeOrganizacionalRecord> UnidadesOrganizacionais => Set<UnidadeOrganizacionalRecord>();
    internal DbSet<IpTransformerIndexRecord>    IpTransformerIndexes   => Set<IpTransformerIndexRecord>();
    internal DbSet<FaturamentoRecord>           Faturamentos           => Set<FaturamentoRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IpBillingDbContext).Assembly);
    }
}
