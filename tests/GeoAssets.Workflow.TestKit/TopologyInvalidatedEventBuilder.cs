using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class TopologyInvalidatedEventBuilder
{
    private string _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private Guid _organizationId = IntegrationMessagingTestDataBuilders.DefaultOrganizationId;
    private string? _scopeId = "sector-01";
    private IReadOnlyList<string> _affectedFeatureIds = ["feat-100", "feat-101"];
    private IReadOnlyList<string> _affectedEdgeKinds = ["connects_to"];
    private Rfc7946BoundingBox? _affectedBounds = new(-38.52, -12.98, -38.49, -12.96);
    private string _reason = "EdgeAdded";
    private DateTimeOffset _invalidatedAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _correlationId = "corr-topo-100";

    public TopologyInvalidatedEventBuilder WithCatalogInstanceId(string catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithOrganizationId(Guid organizationId)
    {
        _organizationId = organizationId;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithScopeId(string? scopeId)
    {
        _scopeId = scopeId;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithAffectedFeatureIds(IReadOnlyList<string> affectedFeatureIds)
    {
        _affectedFeatureIds = affectedFeatureIds;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithAffectedEdgeKinds(IReadOnlyList<string> affectedEdgeKinds)
    {
        _affectedEdgeKinds = affectedEdgeKinds;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithAffectedBounds(Rfc7946BoundingBox? affectedBounds)
    {
        _affectedBounds = affectedBounds;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithReason(string reason)
    {
        _reason = reason;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithInvalidatedAtUtc(DateTimeOffset invalidatedAtUtc)
    {
        _invalidatedAtUtc = invalidatedAtUtc;
        return this;
    }

    public TopologyInvalidatedEventBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public TopologyInvalidatedEvent Build() =>
        new(
            CatalogInstanceId: _catalogInstanceId,
            OrganizationId: _organizationId,
            ScopeId: _scopeId,
            AffectedFeatureIds: _affectedFeatureIds,
            AffectedEdgeKinds: _affectedEdgeKinds,
            AffectedBounds: _affectedBounds,
            Reason: _reason,
            InvalidatedAtUtc: _invalidatedAtUtc,
            CorrelationId: _correlationId);
}
