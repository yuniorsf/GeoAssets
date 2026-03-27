namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by infrastructure providers (PostgreSQL, SQLite, Azure Cosmos …)
/// that can produce an <see cref="IAssetRepository"/> from a connection string.
/// Register in DI; <c>RepositoryPoolPanel</c> discovers all registrations automatically.
/// </summary>
public interface IExternalRepositoryFactory
{
    /// <summary>Human-readable provider name shown in the UI (e.g. "PostgreSQL").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Creates and connects a new <see cref="IAssetRepository"/> instance.
    /// Implementations may apply schema migrations before returning.
    /// </summary>
    IAssetRepository Create(string connectionString);
}
