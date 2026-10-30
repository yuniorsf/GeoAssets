using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class FeatureCatalogMutatedEventBuilder
{
    private string _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private Guid _organizationId = IntegrationMessagingTestDataBuilders.DefaultOrganizationId;
    private string? _scopeId = "sector-01";
    private string _featureId = "feat-100";
    private AssetMutationKind _mutationKind = AssetMutationKind.UpsertFeature;
    private string _name = "Node 100";
    private string _assetTypeId = "network-node";
    private string _layerId = "layer-nodes";
    private AssetGeometrySnapshot? _geometry = new(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []);
    private IReadOnlyDictionary<string, string> _customAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["status"] = "active"
    };
    private Rfc7946BoundingBox? _affectedBounds = new(-38.5014, -12.9714, -38.5014, -12.9714);
    private byte[] _rowVersion = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x2C];
    private DateTimeOffset _mutatedAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _correlationId = "corr-feat-100";

    public FeatureCatalogMutatedEventBuilder WithCatalogInstanceId(string catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithOrganizationId(Guid organizationId)
    {
        _organizationId = organizationId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithScopeId(string? scopeId)
    {
        _scopeId = scopeId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithFeatureId(string featureId)
    {
        _featureId = featureId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithMutationKind(AssetMutationKind mutationKind)
    {
        _mutationKind = mutationKind;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithAssetTypeId(string assetTypeId)
    {
        _assetTypeId = assetTypeId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithLayerId(string layerId)
    {
        _layerId = layerId;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithGeometry(AssetGeometrySnapshot? geometry)
    {
        _geometry = geometry;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithCustomAttributes(IReadOnlyDictionary<string, string> customAttributes)
    {
        _customAttributes = customAttributes;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithAffectedBounds(Rfc7946BoundingBox? affectedBounds)
    {
        _affectedBounds = affectedBounds;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithRowVersion(byte[] rowVersion)
    {
        _rowVersion = rowVersion;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithMutatedAtUtc(DateTimeOffset mutatedAtUtc)
    {
        _mutatedAtUtc = mutatedAtUtc;
        return this;
    }

    public FeatureCatalogMutatedEventBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public FeatureCatalogMutatedEvent Build() =>
        new(
            CatalogInstanceId: _catalogInstanceId,
            OrganizationId: _organizationId,
            ScopeId: _scopeId,
            FeatureId: _featureId,
            MutationKind: _mutationKind,
            Name: _name,
            AssetTypeId: _assetTypeId,
            LayerId: _layerId,
            Geometry: _geometry,
            CustomAttributes: _customAttributes,
            AffectedBounds: _affectedBounds,
            RowVersion: _rowVersion,
            MutatedAtUtc: _mutatedAtUtc,
            CorrelationId: _correlationId);
}
