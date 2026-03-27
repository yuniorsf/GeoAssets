namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by infrastructure providers (PostgreSQL, SQLite, Azure Cosmos …)
/// that can produce an <see cref="IAssetProvider"/> from a connection string.
/// Register in DI; <c>ProviderPoolPanel</c> discovers all registrations automatically.
/// </summary>
public interface IExternalProviderFactory
{
    /// <summary>Human-readable provider name shown in the UI (e.g. "PostgreSQL").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Creates and connects a new <see cref="IAssetProvider"/> instance.
    /// Implementations may apply schema migrations before returning.
    /// </summary>
    IAssetProvider Create(string connectionString);
}
