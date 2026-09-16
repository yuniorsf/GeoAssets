using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server;

/// <summary>
/// Seeds one <see cref="ProjectKind.General"/> Project per organization (XD01-144) — gives
/// every org an immediately-usable Project out of the box, no manual setup step. Idempotent:
/// an org that already has a <see cref="ProjectKind.General"/> Project is left untouched, so
/// this is safe to call on every startup (mirroring <c>GeoIdentitySeeder</c>'s own idempotency)
/// and once more per newly-created organization.
///
/// Lives in <c>GeoAssets.Server</c> rather than either EFCore module: it needs both
/// <see cref="IProjectRepository"/> (Projects.EFCore) and <see cref="IOrganizationRepository"/>
/// (Identity.EFCore), and those two modules don't reference each other.
/// </summary>
public static class DefaultProjectSeeder
{
    /// <summary>
    /// Seeds the default Project for every existing organization. Call once after
    /// <c>host.Build()</c>, alongside <c>SeedGeoIdentityAsync</c>:
    /// <code>
    ///   await app.Services.SeedGeoIdentityAsync();
    ///   await app.Services.SeedDefaultProjectsAsync();
    /// </code>
    /// </summary>
    public static async Task SeedDefaultProjectsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        // EFProjectRepository (resolved below via IProjectRepository) only implements
        // IAsyncDisposable — a synchronous CreateScope()/Dispose() throws InvalidOperationException
        // tearing it down, so this needs the async scope + async-using pair instead.
        await using var scope = services.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var organizations = scope.ServiceProvider.GetRequiredService<IOrganizationRepository>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        foreach (var org in await organizations.GetAllAsync(ct))
            await EnsureDefaultProjectAsync(projects, org.Id, timeProvider, ct);
    }

    /// <summary>
    /// Ensures <paramref name="organizationId"/> has a default <see cref="ProjectKind.General"/>
    /// Project, creating one if it has none. Called both by <see cref="SeedDefaultProjectsAsync"/>
    /// (existing orgs, on startup) and right after a new organization is created (so "brand-new
    /// org has a usable default Project immediately" holds without waiting for a restart).
    /// </summary>
    public static async Task EnsureDefaultProjectAsync(
        IProjectRepository projects, Guid organizationId, TimeProvider timeProvider, CancellationToken ct = default)
    {
        var existing = await projects.GetByOrganizationAsync(organizationId, ct);
        if (existing.Any(p => p.Kind == ProjectKind.General))
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Name           = "Default Project",
            Description    = "Proyecto general predeterminado de la organización.",
            OrganizationId = organizationId,
            Kind           = ProjectKind.General,
            CreatedAt      = now,
            UpdatedAt      = now,
            Providers      = [],
            AssetTypeScope = new ProjectAssetTypeScope
            {
                // The 3 globally-unowned built-ins (OrganizationId == Guid.Empty) — always
                // resolvable regardless of which provider/catalog an org's user-defined
                // AssetTypes happen to live in. Not "Polygon" — GeoAssets.Core.Models.AssetType
                // names it Area.
                VisibleAssetTypeIds = [AssetType.Point.Id, AssetType.Line.Id, AssetType.Area.Id]
            },
            LayerScope = new ProjectLayerScope(), // empty — defers to normal Layer/LayerRule resolution
            ViewState  = new ProjectViewState(),  // defaults
        };

        await projects.AddAsync(project, ct);
    }
}
