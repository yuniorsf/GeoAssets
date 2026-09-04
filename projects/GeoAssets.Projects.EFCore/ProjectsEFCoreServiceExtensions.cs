using GeoAssets.Core.Interfaces;
using GeoAssets.Projects.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Projects;

/// <summary>DI registration helpers for the GeoAssets Project persistence layer.</summary>
public static class ProjectsEFCoreServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ProjectDbContext"/> and the EF Core implementation of
    /// <see cref="IProjectRepository"/>.
    ///
    /// <paramref name="configureDb"/> should set the database provider, e.g.:
    /// <code>
    ///   services.AddProjectPersistence(o => o.UseNpgsql(connectionString));
    /// </code>
    /// </summary>
    public static IServiceCollection AddProjectPersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<ProjectDbContext>(configureDb);

        services.AddScoped<IProjectRepository>(sp =>
            new EFProjectRepository(sp.GetRequiredService<ProjectDbContext>()));

        // Read-only or write-only consumers can depend on just the piece they need instead of
        // the full IProjectRepository.
        services.AddScoped<IProjectReader>(sp => sp.GetRequiredService<IProjectRepository>());
        services.AddScoped<IProjectWriter>(sp => sp.GetRequiredService<IProjectRepository>());

        return services;
    }
}
