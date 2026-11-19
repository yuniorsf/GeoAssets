using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>In-memory <see cref="IOrganizationScopedGeoCoreClient"/> recording every call.</summary>
internal sealed class FakeOrganizationScopedGeoCoreClient : IOrganizationScopedGeoCoreClient
{
    public Dictionary<string, GeoFeature> Features { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<GeoFeature>> Descendants { get; } = new(StringComparer.Ordinal);

    public List<(Guid OrganizationId, string Id)> FeatureCalls { get; } = [];

    public List<(Guid OrganizationId, string Id, string EdgeKind)> DescendantCalls { get; } = [];

    public Func<CancellationToken, Task>? OnCall { get; set; }

    public async Task<GeoFeature?> GetFeatureAsync(Guid organizationId, string id, CancellationToken ct = default)
    {
        FeatureCalls.Add((organizationId, id));
        if (OnCall is { } onCall) await onCall(ct);
        return Features.GetValueOrDefault(id);
    }

    public async Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(Guid organizationId, string id, string edgeKind, CancellationToken ct = default)
    {
        DescendantCalls.Add((organizationId, id, edgeKind));
        if (OnCall is { } onCall) await onCall(ct);
        return Descendants.TryGetValue(id, out var list) ? list : [];
    }
}
