namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Optional seeding step a <see cref="IGeoAssetsModule"/> can also implement (permissions, order types, reference data).
/// Seeders run after <c>Build()</c>, once per enabled module, in the enabled modules' order
/// (<see cref="GeoAssetsModuleHostingExtensions.SeedGeoAssetsModulesAsync"/>).
/// </summary>
public interface IModuleSeeder
{
    /// <summary>Seeds the module's data. <paramref name="services"/> is the host's root provider; create a scope for scoped services.</summary>
    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
