using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Resolves shard- or organization-specific <see cref="IGeoCoreClient"/> instances across
/// federated <c>GeoAssets.Server</c> catalog deployments.
/// </summary>
public interface IGeoCoreClientResolver
{
    IReadOnlyCollection<string> RegisteredCatalogIds { get; }

    IGeoCoreClient Resolve(string? catalogInstanceId = null, Guid organizationId = default);

    bool TryResolve(string? catalogInstanceId, Guid organizationId, [NotNullWhen(true)] out IGeoCoreClient? client);
}
