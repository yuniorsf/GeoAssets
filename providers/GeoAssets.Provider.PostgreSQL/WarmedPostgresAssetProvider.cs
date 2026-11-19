using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// The singleton behind <see cref="PostgresServiceExtensions.AddWarmedPostgresAssetProvider"/>: holds the warmed
/// provider once <see cref="WarmAsync"/> has built it, hands it out validated, and disposes it with the container.
/// </summary>
internal sealed class WarmedPostgresAssetProvider(PostgresProviderFactory factory, string connectionString) : IAsyncDisposable
{
    private IAssetProvider? _warmed;
    private ValidatingAssetProvider? _provider;

    /// <exception cref="InvalidOperationException">The provider has not been warmed yet.</exception>
    public IAssetProvider Provider => _provider
        ?? throw new InvalidOperationException(
            "The PostgreSQL asset provider is not warmed yet: await WarmPostgresAssetProviderAsync right after building the service provider.");

    public async Task WarmAsync(CancellationToken ct)
    {
        if (_provider is not null) return;

        _warmed = await factory.CreateAsync(connectionString, ct).ConfigureAwait(false);
        _provider = new ValidatingAssetProvider(_warmed);
    }

    // ValidatingAssetProvider disposes _warmed once, though the container also disposes it as IAssetProvider.
    public ValueTask DisposeAsync() => _provider?.DisposeAsync() ?? ValueTask.CompletedTask;
}
